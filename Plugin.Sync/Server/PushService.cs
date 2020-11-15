using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HunterPie.Core;
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
        
        private readonly ConcurrentDictionary<int, MonsterModel> cachedMonsters = new ConcurrentDictionary<int, MonsterModel>();
        private readonly List<MonsterModel> pushQueue = new List<MonsterModel>();
        
        private readonly SyncServerClient client = new SyncServerClient();
        
        private CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
        private Thread thread;

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
                ClearQueue();
                this.thread.Start();
            }
            else
            {
                this.cancellationTokenSource?.Cancel();
                ClearQueue();
                this.thread.Join();
            }
            Logger.Trace($"PushService.SetState -> changed");
        }

        public void PushMonster(Monster monster, int index)
        {
            if (string.IsNullOrEmpty(this.SessionId))
            {
                return;
            }

            var mappedMonster = MapMonster(monster, index);
            PushMonster(mappedMonster);
        }

        public void PushMonster(MonsterModel model)
        {
            if (string.IsNullOrEmpty(this.SessionId))
            {
                return;
            }

            lock (this.locker)
            {
                if (this.cachedMonsters.TryGetValue(model.Index, out var existingMonster) && existingMonster.Equals(model))
                {
                    return;
                }

                // TODO: diffing
                this.cachedMonsters[model.Index] = model;
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
                    List<MonsterModel> monsters;

                    lock (this.locker)
                    {
                        monsters = this.pushQueue
                            .GroupBy(m => m.Index)
                            .Select(g => g.Last())
                            .OrderBy(m => m.Index)
                            .ToList();
                        this.pushQueue.Clear();
                    }

                    // wait for changes to appear
                    if (!monsters.Any())
                    {
                        await Task.Delay(50, token);
                        continue;
                    }

                    await this.client.PushChangedMonsters(this.SessionId, monsters, token);
                    Logger.Trace($"PUSH monsters: {monsters.Count} ({sw.ElapsedMilliseconds} ms from last push)");
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

        private void ClearQueue()
        {
            lock (this.locker)
            {
                this.pushQueue.Clear();
                this.cachedMonsters.Clear();
            }
        }

        private static MonsterModel MapMonster(Monster monster, int index)
        {
            return new MonsterModel
            {
                Id = monster.Id,
                Index = index,
                Parts = monster.Parts.Select(MonsterPartModel.FromDomain).ToList(),
                Ailments = monster.Ailments.Select(AilmentModel.FromDomain).ToList()
            };
        }
    }
}
