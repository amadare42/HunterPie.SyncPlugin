using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Plugin.Sync.Model;
using Plugin.Sync.Util;

namespace Plugin.Sync.Server
{
    public class PushService
    {
        public string SessionId { get; set; }

        /// <summary>
        /// Should be used for queue and cached monster synchronization.
        /// </summary>
        private readonly object locker = new object();
        
        private readonly ConcurrentDictionary<string, MonsterModel> cachedMonsters = new ConcurrentDictionary<string, MonsterModel>();
        private readonly List<MonsterModel> pushQueue = new List<MonsterModel>();
        
        private readonly SyncServerClient client = new SyncServerClient();
        
        private CancellationTokenSource cancellationTokenSource;
        private Thread thread;
        
        private readonly DiffModelGenerator diffModelGenerator = new DiffModelGenerator();

        public void SetState(bool state)
        {
            var isRunning = this.cancellationTokenSource != null &&
                            !this.cancellationTokenSource.IsCancellationRequested;
            Logger.Trace($"PushService.SetState -> isRunning: {isRunning}, -> {state}");
            // same state, don't do anything
            if (state == isRunning) return;
            if (state)
            {
                this.cancellationTokenSource?.Cancel();
                this.cancellationTokenSource = new CancellationTokenSource();
                var scanRef = new ThreadStart(() => PushLoop(this.cancellationTokenSource.Token));
                this.thread = new Thread(scanRef) {Name = "SyncPlugin_PushLoop"};
                ClearData();
                this.thread.Start();
            }
            else
            {
                this.cancellationTokenSource?.Cancel();
                ClearData();
                this.thread?.Join();
            }
            Logger.Trace("PushService.SetState -> changed");
        }

        public void PushMonster(MonsterModel model)
        {
            if (string.IsNullOrEmpty(this.SessionId))
            {
                return;
            }

            if (string.IsNullOrEmpty(model.Id))
            {
                Logger.Trace("Monster id is empty");
                return;
            }

            lock (this.locker)
            {
                if (this.cachedMonsters.TryGetValue(model.Id, out var existingMonster) && existingMonster.Equals(model))
                {
                    return;
                }

                this.cachedMonsters[model.Id] = model;
                this.pushQueue.Add(model);
            }
        }

        private async void PushLoop(CancellationToken token)
        {
            var retryCount = 0;
            var sw = new Stopwatch();

            while (true)
            {
                token.ThrowIfCancellationRequested();
                
                if (string.IsNullOrEmpty(this.SessionId))
                {
                    await Task.Delay(50, token);
                    continue;
                }

                try
                {
                    var monsters = ConsumeQueue();

                    // wait for changes to appear
                    if (!monsters.Any())
                    {
                        await Task.Delay(50, token);
                        continue;
                    }

                    var monsterDiffs = this.diffModelGenerator.GetDiffs(monsters);
                    await this.client.PushChangedMonsters(this.SessionId, monsterDiffs, token);
                    Logger.Trace($"PUSH [{GetTraceData(monsterDiffs)}] ({sw.ElapsedMilliseconds} ms from last push)");
                    sw.Restart();
                    if (retryCount != 0)
                    {
                        retryCount = 0;
                        Logger.Log("Connection restored");
                    }
                    // throttling
                    await Task.Delay(300, token);
                }
                catch (OperationCanceledException)
                {
                    Logger.Debug("Push thread stopped.");
                    return;
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error on pushing monsters to server. Will retry after 10 sec when new data is available ({retryCount++}/10): {ex.Message}");
                    try
                    {
                        await Task.Delay(10000, token);
                    }
                    catch (OperationCanceledException)
                    {
                        // not using return, so main catch clause for cancelled operation will be executed
                        continue;
                    }

                    if (retryCount == 10)
                    {
                        Logger.Log("Pushing stopped - no monster data for other members.");
                        return;
                    }
                }
            }
        }

        /// <summary>
        /// Clear queue and normalize to get only latest monster updates
        /// </summary>
        private List<MonsterModel> ConsumeQueue()
        {
            lock (this.locker)
            {
                var monsters = this.pushQueue
                    .GroupBy(m => m.Id)
                    .Select(g => g.Last())
                    .ToList();
                this.pushQueue.Clear();
                return monsters;
            }
        }

        private static string GetTraceData(List<MonsterModel> models) => $"monsters: {models.Count}; parts: {models.Sum(m => m.Parts.Count)}; ailments: {models.Sum(m => m.Ailments.Count)}";

        private void ClearData()
        {
            lock (this.locker)
            {
                this.pushQueue.Clear();
                this.cachedMonsters.Clear();
                this.diffModelGenerator.Clear();
            }
        }
    }
}
