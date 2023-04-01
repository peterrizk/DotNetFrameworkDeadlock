using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Web.Mvc;

namespace Deadlock.Controllers
{
    public class HomeController : Controller
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
        int concurrencyNumber = 100;
        public async Task<ActionResult> Index()
        {
            Logger.Info("commenced");
            var id = Thread.CurrentThread.ManagedThreadId;
            var IsBackgroundThread = Thread.CurrentThread.IsBackground;
            var hasSyncContext = SynchronizationContext.Current != null;
            Logger.Info($"Main START threadid: {id}  IsBackgroundThread: {IsBackgroundThread} syncContext Exisits: {hasSyncContext}");


            // ******* you can control how many cpu bound threads and how many io threads (for http calls) are available here if you need too ************
            //int nWorkers; // number of processing threads
            //int nCompletions; // number of I/O threads
            //ThreadPool.GetMaxThreads(out nWorkers, out nCompletions);
            //nWorkers = 10;
            //    nCompletions = 10;
            //ThreadPool.SetMaxThreads(nWorkers, nCompletions);
            // *************************************************************************

            var obj = ParallelVersion(false); //dead locks
            //var obj = ParallelVersion(true); // no dead lock but need to be careful to configureAwait(false)
            //var obj = SingleVersion(false); //dead locks
            //var obj = SingleVersion(true); // no dead locks but need to be careful to configureAwait(false)
            //var obj = await TaskRunWhenAll(false); //no dead locks
            //var obj = await TaskRunWhenAll(true); //no dead locks 

            //Speedwise both seem to be similar


            Logger.Info($"Main END threadid: {id} IsBackgroundThread: {IsBackgroundThread} syncContext Exisits: {hasSyncContext}");

            return Json(obj, JsonRequestBehavior.AllowGet);
        }


        /// <summary>
        /// This scenario causes a deadlock if removeSyncContext = false
        /// </summary>
        /// <param name="removeSyncContext"></param>
        /// <returns></returns>
        public dynamic ParallelVersion(bool removeSyncContext = false)
        {
            var list = new ConcurrentBag<Tuple<SynchronizationContext, int>>() {
                new Tuple<SynchronizationContext,int>(SynchronizationContext.Current, -1) };

            var concurrency = Enumerable.Range(1, concurrencyNumber);
            var timer = new Stopwatch();
            timer.Start();
            Parallel.ForEach(concurrency, index =>
               list.Add(new Tuple<SynchronizationContext, int>(SyncCall(removeSyncContext), index)));
            timer.Stop();
            
            return
                new { ThreadsAndSyncontext = $"exists:{list.Count(i => i.Item1 != null)} doesn't:{list.Count(i => i.Item1 == null)} total:{list.Count()}", 
                    time = $"mins: {timer.Elapsed.Minutes} sec: {timer.Elapsed.Seconds}", 
                    data = list.Select(t => new { i = t.Item2, exists = t.Item1 != null }).OrderBy(i => i.i) };
        }

        /// <summary>
        /// This scenario also causes a deadlock if removeSyncContext = false
        /// </summary>
        /// <param name="removeSyncContext"></param>
        /// <returns></returns>
        public dynamic SingleVersion(bool removeSyncContext = false)
        {
            var timer = new Stopwatch();
            timer.Start();
            SyncCall(removeSyncContext);
            timer.Stop();

            return
                new
                {
                    syncontext = SynchronizationContext.Current != null,
                    time = $"mins: {timer.Elapsed.Minutes} sec: {timer.Elapsed.Seconds}"
                };
        }

        /// <summary>
        /// This scenario wont deadlock
        /// </summary>
        /// <param name="removeSyncContext"></param>
        /// <returns></returns>
        public async Task<dynamic> TaskRunWhenAll(bool removeSyncContext = false)
        {
            var list = new ConcurrentBag<Tuple<SynchronizationContext, int>>() {
                new Tuple<SynchronizationContext,int>(SynchronizationContext.Current, -1) };

            var concurrency = Enumerable.Range(1, concurrencyNumber);
            var timer = new Stopwatch();
            var tasks = new List<Task>();
            timer.Start();

            var ix = 1;
            foreach (var item in concurrency)
            {
                var task = Task.Run(() => list.Add(new Tuple<SynchronizationContext, int>(SyncCall(removeSyncContext), ix)));
                tasks.Add(task);
                ix++;
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);

            timer.Stop();

            return
                new
                {
                    syncontext = $"exists:{list.Count(i => i.Item1 != null)} doesn't:{list.Count(i => i.Item1 == null)} total:{list.Count()}",
                    time = $"mins: {timer.Elapsed.Minutes} sec: {timer.Elapsed.Seconds}",
                    data = list.Select(t => new { i = t.Item2, exists = t.Item1 != null }).OrderBy(i => i.i)
                };
        }


        private SynchronizationContext SyncCall(bool removeSyncContext = false)
        {
            var id = Thread.CurrentThread.ManagedThreadId;
            var IsBackgroundThread = Thread.CurrentThread.IsBackground;
            var hasSyncContext = SynchronizationContext.Current != null;
            Logger.Info($"SyncCall START threadid: {id}  IsBackgroundThread: {IsBackgroundThread} syncContext Exisits: {hasSyncContext}");

            
            var sctx = SynchronizationContext.Current;
            AsyncWork(removeSyncContext).Wait();
            Logger.Info($"SyncCall End threadid: {id} IsBackgroundThread: {IsBackgroundThread} syncContext Exisits: {hasSyncContext}");
            return sctx;
        }


        private async Task AsyncWork(bool removeSyncContext = false)
        {
            var id = Thread.CurrentThread.ManagedThreadId;
            var IsBackgroundThread = Thread.CurrentThread.IsBackground;
            var hasSyncContext = SynchronizationContext.Current != null;
            Logger.Info($"AsyncWork START threadid: {id} IsBackgroundThread: {IsBackgroundThread} syncContext Exisits: {hasSyncContext}");
            var client = new HttpClient();
            if(removeSyncContext)
                await client.GetAsync("https://google.com").ConfigureAwait(false);
            else
                await client.GetAsync("https://google.com").ConfigureAwait(true);
            Logger.Info($"AsyncWork END threadid: {id} IsBackgroundThread: {IsBackgroundThread} syncContext Exisits: {hasSyncContext}");
            //Thread.Sleep(5000);
        }
    }
}
