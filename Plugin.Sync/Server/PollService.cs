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

        private readonly SemaphoreSlim semaphore = new SemaphoreSlim(1);
        private List<MonsterModel> polledMonsters = CreateDefaultMonstersCollection();
        private readonly SyncServerClient client = new SyncServerClient();
        private Thread thread;

        public void SetState(bool state)
        {
            var isRunning = this.thread?.IsAlive ?? false;
            // same state, don't do anything
            if (isRunning == state) return;
            if (state)
            {
                var scanRef = new ThreadStart(PollLoop);
                this.thread = new Thread(scanRef) {Name = "SyncPlugin_PollLoop"};
                this.thread.Start();
            }
            else
            {
                this.thread?.Abort();
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

        private async void PollLoop()
        {
            var pollId = Guid.NewGuid().ToString();
            var retryCount = 0;
            var sw = new Stopwatch();

            while (true)
            {
                // wait for session id
                if (string.IsNullOrEmpty(this.SessionId))
                {
                    await Task.Delay(500);
                    continue;
                }

                try
                {
                    // throttling
                    await Task.Delay(500);
                    sw.Restart();
                    var changedMonsters = await this.client.PollMonsterChanges(this.SessionId, pollId);
                    if (changedMonsters == null)
                    {
                        continue;
                    }

                    UpdateMonsters(changedMonsters);
                    Logger.Trace($"Monsters updated: {changedMonsters.Count} ({sw.ElapsedMilliseconds} ms after request started)");
                    if (retryCount != 0)
                    {
                        retryCount = 0;
                        Logger.Log("Connection restored");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error on polling changes. Waiting for 10 sec before retry ({retryCount++}/10). : {ex.Message}");
                    await Task.Delay(10000);
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
