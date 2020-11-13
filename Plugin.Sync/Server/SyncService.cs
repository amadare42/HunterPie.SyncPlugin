using System;
using System.Collections.Generic;
using HunterPie.Core;
using Plugin.Sync.Model;
using Plugin.Sync.Util;

namespace Plugin.Sync.Server
{
    public class SyncService
    {
        private readonly PushService push = new PushService();
        private readonly PollService poll = new PollService();
        public SyncServiceMode Mode { get; private set; }

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
            if (this.Mode == mode) return false;
            
            switch (mode)
            {
                case SyncServiceMode.Idle:
                    this.poll.SetState(false);
                    this.push.SetState(false);
                    break;
                case SyncServiceMode.Poll:
                    this.poll.SetState(true);
                    this.push.SetState(false);
                    break;
                case SyncServiceMode.Push:
                    this.poll.SetState(false);
                    this.push.SetState(true);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
            }

            this.Mode = mode;
            return true;
        }

        public void PushMonster(Monster monster, int index) => this.push.PushMonster(monster, index);

        public void PushMonster(MonsterModel model) => this.push.PushMonster(model);

        /// <summary>
        /// Take temporary ownership for cached monsters
        /// </summary>
        public Borrow<List<MonsterModel>> BorrowMonsters() => this.poll.BorrowMonsters();
    }
}
