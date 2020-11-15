using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Plugin.Sync.Model;
using Plugin.Sync.Util;

namespace Plugin.Sync.Server
{
    public class PollService
    {
        public string SessionId { get; set; }

        /// <summary>
        /// Should be used for polled monster synchronization.
        /// </summary>
        private readonly SemaphoreSlim semaphore = new SemaphoreSlim(1);
        private List<MonsterModel> polledMonsters = CreateDefaultMonstersCollection();

        private readonly SyncServerClient client = new SyncServerClient();

        private Thread thread;
        private CancellationTokenSource cancellationTokenSource;

        /// <summary>
        /// NOTE: This is not thread-safe.
        /// </summary>
        public void SetState(bool state)
        {
            var isRunning = this.cancellationTokenSource != null &&
                            !this.cancellationTokenSource.IsCancellationRequested;
            // same state, don't do anything
            if (state == isRunning) return;
            if (state)
            {
                this.cancellationTokenSource?.Cancel();
                this.cancellationTokenSource = new CancellationTokenSource();
                var cancelToken = this.cancellationTokenSource.Token;
                var scanRef = new ThreadStart(() => PollLoop(cancelToken));
                this.thread = new Thread(scanRef) {Name = "SyncPlugin_PollLoop"};
                this.thread.Start();
            }
            else
            {
                this.cancellationTokenSource?.Cancel();
                this.semaphore.Wait();
                this.polledMonsters = CreateDefaultMonstersCollection();
                this.semaphore.Release();
            }
        }

        public Borrow<List<MonsterModel>> BorrowMonsters()
        {
            this.semaphore.Wait();
            return new Borrow<List<MonsterModel>>(this.polledMonsters, ReleaseSemaphore);
        }

        private void ReleaseSemaphore() => this.semaphore.Release();

        private async void PollLoop(CancellationToken token)
        {
            var pollId = Guid.NewGuid().ToString();
            var retryCount = 0;
            var sw = new Stopwatch();
            Logger.Debug($"Started new poll with '{pollId}'");

            while (true)
            {
                token.ThrowIfCancellationRequested();
                
                // wait for session id
                if (string.IsNullOrEmpty(this.SessionId))
                {
                    await Task.Delay(500, token);
                    continue;
                }

                try
                {
                    // throttling
                    await Task.Delay(500, token);
                    sw.Restart();
                    var changedMonsters = await this.client.PollMonsterChanges(this.SessionId, pollId, token);
                    if (changedMonsters == null)
                    {
                        Logger.Trace($"No monsters updates. ({sw.ElapsedMilliseconds} ms after request started)");
                        continue;
                    }

                    UpdateMonsters(changedMonsters);
                    Logger.Trace(
                        $"Monsters updated: {changedMonsters.Count} ({sw.ElapsedMilliseconds} ms after request started)");
                    if (retryCount != 0)
                    {
                        retryCount = 0;
                        Logger.Log("Connection restored");
                    }
                }
                catch (OperationCanceledException)
                {
                    Logger.Debug($"Poll with '{pollId}' closed.");
                    return;
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error on polling changes. Waiting for 10 sec before retry ({retryCount++}/10). : {ex.Message}");
                    try
                    {
                        await Task.Delay(10000, token);
                    }
                    catch (OperationCanceledException)
                    {
                        continue;
                    }
                    
                    if (retryCount == 10)
                    {
                        Logger.Log("Polling stopped - no monster data updates.");
                        return;
                    }
                }
            }
        }

        private static List<MonsterModel> CreateDefaultMonstersCollection() => new List<MonsterModel>
        {
            new MonsterModel {Index = 0}, new MonsterModel {Index = 1}, new MonsterModel {Index = 2},
        };

        private void UpdateMonsters(List<MonsterModel> monsters)
        {
            using var borrow = BorrowMonsters(); 
            foreach (var monsterModel in monsters)
            {
                this.polledMonsters[monsterModel.Index] = monsterModel;
            }
        }
    }
}
