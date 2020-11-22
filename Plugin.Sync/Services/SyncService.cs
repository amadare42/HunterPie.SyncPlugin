using System;
using System.Collections.Generic;
using Plugin.Sync.Model;
using Plugin.Sync.Util;

namespace Plugin.Sync.Services
{
    public class SyncService
    {
        private readonly PushService push = new PushService();
        private readonly PollService poll = new PollService();
        public SyncServiceMode Mode { get; private set; }
        private readonly object locker = new object();

        public void SetSessionId(string sessionId)
        {
            this.push.SessionId = sessionId;
            this.poll.SessionId = sessionId;
        }

        /// <summary>
        /// Set sync mode (e.g. direction: push/poll). Returns true if changed.
        /// </summary>
        public bool SetMode(SyncServiceMode mode)
        {
            lock (this.locker)
            {
                if (this.Mode == mode) return false;
            
                switch (mode)
                {
                    case SyncServiceMode.Idle:
                        this.poll.SetEnabled(false);
                        this.push.SetEnabled(false);
                        break;
                    case SyncServiceMode.Poll:
                        this.poll.SetEnabled(true);
                        this.push.SetEnabled(false);
                        break;
                    case SyncServiceMode.Push:
                        this.poll.SetEnabled(false);
                        this.push.SetEnabled(true);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
                }

                this.Mode = mode;
                return true;
            }
        }

        public void PushMonster(MonsterModel model) => this.push.PushMonster(model);

        /// <summary>
        /// Take temporary ownership for cached monsters
        /// </summary>
        public Borrow<List<MonsterModel>> BorrowMonsters() => this.poll.BorrowMonsters();
    }
}
