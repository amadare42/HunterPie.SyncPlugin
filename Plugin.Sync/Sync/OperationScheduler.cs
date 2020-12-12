using System;
using System.Threading;
using System.Threading.Tasks;
using Plugin.Sync.Logging;
using Plugin.Sync.Util;

namespace Plugin.Sync.Sync
{
    /// <summary>
    /// Manages async cancellable tasks. Making sure that only one task is executed at a time.
    /// </summary>
    public class OperationScheduler
    {
        private static readonly IClassLogger Logger = LogManager.GetCurrentClassLogger();
        
        private Task currentTask = Task.CompletedTask;
        private CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
        private int actionCounter;
        private readonly SemaphoreSlim semaphore = new SemaphoreSlim(1);

        private Task WaitForFinish()
        {
            return this.currentTask;
        }

        public async void CancelCurrentAction()
        {
            await this.semaphore.WaitAsync();
            if (!this.currentTask.IsCompleted)
            {
                this.cancellationTokenSource.Cancel();
                Logger.Trace("Cancellation requested");
            }
            else
            {
                Logger.Trace($"No cancellation - operation [{this.actionCounter}] is finished");
            }
            
            this.semaphore.Release();
        }
        
        public Func<Task> CreateAction(Action action)
        {
            Func<Task> resultAction = async () =>
            {
                var actionId = Interlocked.Increment(ref this.actionCounter);
                try
                {
                    await this.semaphore.WaitAsync();
                    Logger.Trace($"Transition [{actionId}] started");
                    await WaitForFinish();
                    // not cancellable, so new cancellation token isn't created
                    action();
                    this.currentTask = Task.CompletedTask;
                    Logger.Trace($"Transition [{actionId}] finished");
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error on handling action [{actionId}]: " + ex);
                }
                finally
                {
                    this.semaphore.Release();
                }
            };
#if DEBUG
            resultAction = ReflectionsHelper.WrapActionWithName(resultAction, action.Method.Name);
#endif

            return resultAction;
        }
        
        public Func<Task> CreateAction(Func<CancellationToken, Task> action)
        {
            Func<Task> resultAction = async () =>
            {
                var actionId = Interlocked.Increment(ref this.actionCounter);
                try
                {
                    await this.semaphore.WaitAsync();
                    Logger.Trace($"Transition [{actionId}] started");

                    await WaitForFinish();

                    this.cancellationTokenSource = new CancellationTokenSource();
                    this.currentTask = action(this.cancellationTokenSource.Token)
                        .ContinueWith(OnOperationFinished(actionId));
                }
                finally
                {
                    this.semaphore.Release();
                }
            };
#if DEBUG
            resultAction = ReflectionsHelper.WrapActionWithName(resultAction, action.Method.Name);
#endif

            return resultAction;
        }

        private static Action<Task> OnOperationFinished(int actionId) => task =>
            {
                if (task.Exception != null)
                {
                    Logger.Error($"Error on handling action [{actionId}]: " + task.Exception.Flatten().InnerException);
                }
                else
                {
                    Logger.Trace($"Transition [{actionId}] finished");
                }
            };
    }
}