using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HunterPie.Core;
using Plugin.Sync.Model;

namespace Plugin.Sync.Server
{
    public class PushService
    {
        public string SessionId { get; set; }

        ConcurrentDictionary<int, MonsterModel> CachedMonsters = new ConcurrentDictionary<int, MonsterModel>();

        List<MonsterModel> PushQueue = new List<MonsterModel>();

        private object locker = new object();

        SyncServerClient client = new SyncServerClient();
        private Thread thread;

        public void SetState(bool state)
        {
            var isRunning = this.thread?.IsAlive ?? false;
            // same state, don't do anything
            if (isRunning == state) return;
            if (state)
            {
                var scanRef = new ThreadStart(PushLoop);
                this.thread = new Thread(scanRef) {Name = "SyncPlugin_PushLoop"};
                this.thread.Start();
            }
            else
            {
                this.thread?.Abort();
                lock (this.locker)
                {
                    this.PushQueue.Clear();
                    this.CachedMonsters.Clear();
                }
            }
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
                if (this.CachedMonsters.TryGetValue(model.Index, out var existingMonster) && existingMonster.Equals(model))
                {
                    return;
                }

                // TODO: diffing
                this.CachedMonsters[model.Index] = model;
                this.PushQueue.Add(model);
            }
        }

        public async void PushLoop()
        {
            var retryCount = 0;

            while (true)
            {
                if (string.IsNullOrEmpty(this.SessionId))
                {
                    continue;
                }

                try
                {
                    List<MonsterModel> monsters;

                    lock (this.locker)
                    {
                        monsters = this.PushQueue
                            .GroupBy(m => m.Index)
                            .Select(g => g.Last())
                            .OrderBy(m => m.Index)
                            .ToList();
                        this.PushQueue.Clear();
                    }

                    if (!monsters.Any())
                    {
                        await Task.Delay(50);
                        continue;
                    }

                    Util.Logger.Trace($"PUSH RQ monsters: {monsters.Count}");
                    await this.client.PushChangedMonsters(this.SessionId, monsters);
                    if (retryCount != 0)
                    {
                        retryCount = 0;
                        Util.Logger.Log("Connection restored");
                    }
                }
                catch (Exception ex)
                {
                    Util.Logger.Error($"Error on pushing monsters to server. Will retry after 10 sec when new data is available({retryCount++}/10): {ex.Message}");
                    await Task.Delay(10000);
                    if (retryCount == 10)
                    {
                        Util.Logger.Log("Pushing stopped - no monster data for other members.");
                        return;
                    }
                }
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
