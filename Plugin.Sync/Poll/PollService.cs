using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Plugin.Sync.Connectivity.Model.Messages;
using Plugin.Sync.Logging;
using Plugin.Sync.Model;
using Plugin.Sync.Util;

namespace Plugin.Sync.Poll
{
    public class PollService
    {
        /// <summary>
        /// Should be used for polled monster synchronization.
        /// </summary>
        private readonly SemaphoreSlim semaphore = new SemaphoreSlim(1);
        private readonly List<MonsterModel> polledMonsters = new List<MonsterModel>();
        
        private readonly Stopwatch stopwatch = new Stopwatch();

        public void HandlePushMessage(PushMonstersMessage push)
        {
            var changedMonsters = push.Data;
            if (Logger.IsEnabled(LogLevel.Trace))
            {
                Logger.Trace(
                    $"Monsters updated: {changedMonsters.Count} ({this.stopwatch.ElapsedMilliseconds} ms after last update)");
            }

            UpdateMonsters(changedMonsters);
            this.stopwatch.Restart();
        }

        public Borrow<List<MonsterModel>> BorrowMonsters()
        {
            this.semaphore.Wait();
            return new Borrow<List<MonsterModel>>(this.polledMonsters, ReleaseSemaphore);
        }

        private void ReleaseSemaphore() => this.semaphore.Release();
        
        public void UpdateMonsters(List<MonsterModel> monsters)
        {
            using var borrow = BorrowMonsters();
            
            foreach (var upd in monsters)
            {
                var existingMonster = this.polledMonsters.FirstOrDefault(m => m.Id == upd.Id);
                if (existingMonster == null)
                {
                    this.polledMonsters.Add(upd);
                }
                else
                {
                    existingMonster.UpdateWith(upd);
                }
            }
        }
        
        public void ClearCache()
        {
            this.semaphore.Wait();
            this.polledMonsters.Clear();
            this.semaphore.Release();
        }
    }
}