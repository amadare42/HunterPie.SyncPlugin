using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HunterPie.Core;
using Plugin.Sync.Connectivity;
using Plugin.Sync.Connectivity.Model;
using Plugin.Sync.Model;
using Plugin.Sync.Util;

namespace Plugin.Sync.Services
{
    public class PushService
    {
        private const int MinThrottling = 150;
        
        public event EventHandler<EventArgs> OnSendFailed; 
        
        private readonly IDomainWebsocketClient client;
        private readonly DiffService diffService = new DiffService();

        /// <summary>
        /// Should be used for queue and cached monster synchronization.
        /// </summary>
        private readonly object cacheLocker = new object();
        private readonly ConcurrentDictionary<string, MonsterModel> cachedMonsters = new ConcurrentDictionary<string, MonsterModel>();
        private readonly List<MonsterModel> pushQueue = new List<MonsterModel>();
        
        private readonly SemaphoreSlim dataAvailableEvent = new SemaphoreSlim(0, 1);
        private CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
        private string sessionId;
        private Thread thread;

        public PushService(IDomainWebsocketClient client)
        {
            this.client = client;
        }

        public void StartPushLoop(string sessionId)
        {
            this.sessionId = sessionId;
            this.thread?.Join();
            var scanRef = new ThreadStart(() => PushLoop(this.cancellationTokenSource.Token));
            this.thread = new Thread(scanRef) {Name = "SyncPlugin_PushLoop"};
            ClearCache();
            this.thread.Start();
        }

        public void StopPushLoop()
        {
            this.cancellationTokenSource.Cancel();
            this.cancellationTokenSource = new CancellationTokenSource();
            ClearCache();
        }
        
        public void PushMonster(MonsterModel model)
        {
            if (string.IsNullOrEmpty(model.Id))
            {
                Logger.Debug("Monster id is empty");
                return;
            }

            lock (this.cacheLocker)
            {
                if (this.cachedMonsters.TryGetValue(model.Id, out var existingMonster) && existingMonster.Equals(model))
                {
                    return;
                }

                this.cachedMonsters[model.Id] = model;
                this.pushQueue.Add(model);
                
                // notify that new data is available if push loop is waiting for it
                // this should be done inside of lock, since check & release operation is not atomic
                if (this.dataAvailableEvent.CurrentCount == 0) this.dataAvailableEvent.Release();
            }
        }
        
        private async void PushLoop(CancellationToken token)
        {
            var sw = new Stopwatch();
            
            // a bit more that scan delay to increase chance of pushing 2 or 3 monsters at a time
            var throttling = UserSettings.PlayerConfig.Overlay.GameScanDelay + 20;
            throttling = throttling < MinThrottling ? MinThrottling : throttling;
            
            while (!token.IsCancellationRequested)
            {
                try
                {
                    token.ThrowIfCancellationRequested();

                    // wait for changes to appear
                    await this.dataAvailableEvent.WaitAsync(token);
                    var monsters = ConsumeQueue();
                    if (!monsters.Any())
                        continue;

                    // sending diffs
                    var monsterDiffs = this.diffService.GetDiffs(monsters);
                    var dto = new PushMonstersMessage(this.sessionId, monsterDiffs);
                    await this.client.Send(dto, token);

                    if (Logger.IsEnabled(LogLevel.Trace))
                    {
                        Logger.Trace($"PUSH [{GetTraceData(monsterDiffs)}] ({sw.ElapsedMilliseconds} ms from last push)");
                    }

                    sw.Restart();

                    await Task.Delay(throttling, token);
                }
                catch (OperationCanceledException)
                {
                    Logger.Trace("PushLoop cancelled");
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error on sending data: {ex}.");
                    this.OnSendFailed?.Invoke(this, EventArgs.Empty);
                }
            }
        }
        
        /// <summary>
        /// Clear queue and normalize to get only latest monster updates
        /// </summary>
        private List<MonsterModel> ConsumeQueue()
        {
            lock (this.cacheLocker)
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
        
        public void ClearCache()
        {
            lock (this.cacheLocker)
            {
                this.pushQueue.Clear();
                this.cachedMonsters.Clear();
                this.diffService.Clear();
            }
        }
    }
}