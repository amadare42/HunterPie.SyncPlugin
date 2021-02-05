using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HunterPie.Core;
using HunterPie.Core.Events;
using HunterPie.Plugins;
using Plugin.Sync.Connectivity;
using Plugin.Sync.Logging;
using Plugin.Sync.Model;
using Plugin.Sync.Sync;

namespace Plugin.Sync
{
    public class SyncPlugin : IPlugin
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public Game Context { get; set; }

        private readonly SyncService syncService;
        private readonly SemaphoreSlim updateSyncStateSemaphore = new SemaphoreSlim(1);

        public SyncPlugin()
        {
            ConfigService.Load();
            var wsClient = new DomainWebsocketClient(ConfigService.GetWsUrl());
            this.syncService = new SyncService(wsClient);
            this.syncService.OnSyncModeChanged += OnSyncModeChanged;
        }

        public void Initialize(Game context)
        {
            this.Context = context;

            this.Context.Player.OnZoneChange += OnZoneChange;
            this.Context.Player.OnSessionChange += OnSessionChange;
            this.Context.Player.OnCharacterLogout += OnCharacterLogout;
            this.Context.Player.OnCharacterLogin += OnCharacterLogin;
            foreach (var member in this.Context.Player.PlayerParty.Members)
            {
                member.OnSpawn += OnMemberSpawn;
            }
            
            foreach (var monster in this.Context.Monsters)
            {
                monster.OnMonsterScanFinished += OnMonsterScanFinished;
            }
        }

        public void Unload()
        {
            this.Context.Player.OnZoneChange -= OnZoneChange;
            this.Context.Player.OnSessionChange -= OnSessionChange;
            this.Context.Player.OnCharacterLogout -= OnCharacterLogout;
            this.Context.Player.OnCharacterLogin -= OnCharacterLogin;
            
            if (this.Context.Player.PlayerParty.Members != null)
            {
                foreach (var member in this.Context.Player.PlayerParty.Members)
                {
                    member.OnSpawn -= OnMemberSpawn;
                }
            }
            
            foreach (var monster in this.Context.Monsters)
            {
                monster.OnMonsterScanFinished -= OnMonsterScanFinished;
            }

            this.Context = null;
        }

        private async void OnZoneChange(object source, EventArgs args)
        {
            // This is needed because player scan is performed in another thread. And it isn't done yet,
            // some Context.Player.* data may not be fully initialized yet.
            await WaitForPlayerScan();
            
            if (string.IsNullOrEmpty(this.Context?.Player.SessionID) && !(this.Context?.Player.InPeaceZone ?? false))
            {
                Logger.Debug("Zone changed, but session id is missing. Wait for next event.");
            } 
            else if (this.Context?.Player.PlayerParty.Members.All(m => !m.IsInParty) ?? true)
            {
                Logger.Debug("Zone changed, but party is not loaded yet. Wait for next event.");
            }
            else
            {
                UpdateSyncState($"zone change ({this.Context?.Player.ZoneName})");
            }
        }

        /// <summary>
        /// Hold thread until next player scan is finished.
        /// </summary>
        private async Task WaitForPlayerScan()
        {
            var semaphore = new SemaphoreSlim(0);
            void Trigger(object sender, EventArgs args)
            {
                if (this.Context?.Player != null)
                {
                    this.Context.Player.OnPlayerScanFinished -= Trigger;
                }

                semaphore.Release();
            }

            if (this.Context?.Player == null)
                return;

            this.Context.Player.OnPlayerScanFinished += Trigger;

            var evtReceived = await semaphore.WaitAsync(TimeSpan.FromSeconds(5));
            if (!evtReceived)
            {
                Logger.Warn("Timeout on expecting player scan!");
            }
            Logger.Trace("Finished waiting for player scan");
        }
        
        private void OnMemberSpawn(object source, PartyMemberEventArgs args) => UpdateSyncState($"{args.Name} spawn");
        
        private void OnSessionChange(object source, EventArgs args) => UpdateSyncState("session change");
        
        private void OnCharacterLogout(object source, EventArgs args) => UpdateSyncState("character logout");
        
        private void OnCharacterLogin(object source, EventArgs args) => UpdateSyncState("character login");
        
        private void OnMonsterScanFinished(object source, EventArgs args)
        {
            var monster = (Monster) source;
            if (this.syncService.Mode == SyncServiceMode.Idle
                || string.IsNullOrEmpty(monster.Id)
                || !IsGameInSyncableState())
            {
                return;
            }

            var isLeader = IsLeader();
            if (isLeader && this.syncService.Mode == SyncServiceMode.Push)
            {
                var monsterModel = MapMonster(monster);
                this.syncService.PushMonster(monsterModel);
            }
            else if (!isLeader && this.syncService.Mode == SyncServiceMode.Poll)
            {
                PullData(monster);
            }
        }

        private bool IsLeader() => this.Context?.Player.PlayerParty.IsLocalHost ?? false;

        private const int TrainingAreaZoneId = 504;

        private bool IsGameInSyncableState() => !this.Context.Player.InPeaceZone
                                                && this.Context.Player.ZoneID != TrainingAreaZoneId
                                                && !string.IsNullOrEmpty(this.Context.Player.SessionID)
                                                #if DEBUG
                                                ;
                                                #else
                                                && this.Context.Player.PlayerParty.Size > 1;
                                                #endif

        private void UpdateSessionId()
        {
            var playerName = this.Context.Player.PlayerParty.Members.FirstOrDefault(m => m.IsPartyLeader)?.Name;
            var playerPostfix = playerName != null ? $":{playerName}" : "";
            var sessionId = $"{this.Context.Player.SessionID}{playerPostfix}";
            this.syncService.SetSessionId(sessionId);
        }

        private void UpdateSyncState(string trigger)
        {
            this.updateSyncStateSemaphore.Wait();
            try
            {
                Logger.Log($"Event: {trigger}");
                UpdateSessionId();
                if (!string.IsNullOrEmpty(this.Context?.Player.Name))
                {
                    this.syncService.PlayerName = this.Context.Player.Name;
                }

                if (!IsGameInSyncableState())
                {
                    this.syncService.SetMode(SyncServiceMode.Idle);
                }
                else if (IsLeader())
                {
                    this.syncService.SetMode(SyncServiceMode.Push);
                }
                else
                {
                    this.syncService.SetMode(SyncServiceMode.Poll);
                }
            }
            finally
            {
                this.updateSyncStateSemaphore.Release();
            }
        }

        private void OnSyncModeChanged(object sender, SyncServiceMode mode)
        {
            Logger.Info($"SyncState: {mode}");
            if (mode == SyncServiceMode.Push)
            {
                // if became leader, push all monsters
                foreach (var monster in this.Context.Monsters)
                {
                    var monsterModel = MapMonster(monster);
                    this.syncService.PushMonster(monsterModel);
                }
            }
        }

        private void PullData(Monster monster)
        {
            using var monstersBorrow = this.syncService.BorrowMonsters();
            var monsterModel = monstersBorrow.Value.FirstOrDefault(m => m.Id == monster.Id);
            if (monsterModel != null)
            {
                UpdateFromMonsterModel(monster, monsterModel);
                // we don't need to process same updates again, so we can clear them
                monstersBorrow.Value.Remove(monsterModel);
            }
        }

        private static void UpdateFromMonsterModel(Monster monster, MonsterModel monsterModel)
        {
            if (monster.Parts.Count == 0 || monster.Ailments.Count == 0)
            {
                Logger.Trace("Monster isn't initialized, update skipped");
                return;
            }
            
            for (var i = 0; i < monster.Parts.Count; i++)
            {
                var upd = monsterModel.Parts.FirstOrDefault(p => p.Index == i);
                if (upd != null)
                {
                    monster.Parts[i].Health = upd.Health;
                }
            }
            
            for (var i = 0; i < monster.Ailments.Count; i++)
            {
                var upd = monsterModel.Ailments.FirstOrDefault(p => p.Index == i);
                if (upd != null)
                {
                    monster.Ailments[i].Buildup = upd.Buildup;
                }
            }
        }
        
        private static MonsterModel MapMonster(Monster monster)
        {
            return new MonsterModel
            {
                Id = monster.Id,
                Parts = monster.Parts.Select(MonsterPartModel.FromCoreModel).ToList(),
                Ailments = monster.Ailments.Select(AilmentModel.FromCoreModel).ToList()
            };
        }
    }
}