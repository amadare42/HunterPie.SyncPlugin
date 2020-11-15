using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HunterPie.Core;
using HunterPie.Core.Events;
using HunterPie.Memory;
using HunterPie.Plugins;
using Plugin.Sync.Model;
using Plugin.Sync.Server;
using Plugin.Sync.Util;

namespace Plugin.Sync
{
    public class SyncPlugin : IPlugin
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public Game Context { get; set; }

        private readonly Action<Part, MonsterPartModel> UpdateTenderizePart;
        private readonly Action<Monster> LeadersMonsterUpdate;
        private readonly Action<Monster> PeersMonsterUpdate;

        private readonly SyncService syncService;

        public SyncPlugin()
        {
            this.syncService = new SyncService();

            this.UpdateTenderizePart = ReflectionsHelper.CreateUpdateTenderizePartFn();
            this.LeadersMonsterUpdate = ReflectionsHelper.CreateLeadersMonsterUpdateFn();
            this.PeersMonsterUpdate = ReflectionsHelper.CreatePeersMonsterUpdateFn();
            ConfigService.Load();
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

            Task.Run(WaitForScan);
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

            this.Context = null;
        }

        private void OnZoneChange(object source, EventArgs args)
        {
            if (string.IsNullOrEmpty(this.Context?.Player.SessionID) && !(this.Context?.Player.InPeaceZone ?? false))
            {
                Logger.Debug("Zone changed, but session id is missing. Wait for next event.");
            } 
            else
            {
                UpdateSyncState("zone change");
            }
        }
        
        private void OnMemberSpawn(object source, PartyMemberEventArgs args) => UpdateSyncState($"{args.Name} spawn");
        
        private void OnSessionChange(object source, EventArgs args) => UpdateSyncState("session change");
        
        private void OnCharacterLogout(object source, EventArgs args) => UpdateSyncState("character logout");
        
        private void OnCharacterLogin(object source, EventArgs args) => UpdateSyncState("character login");

        private bool IsLeader() => this.Context?.Player.PlayerParty.Members.Any(m => m.IsInParty && m.IsMe && m.IsPartyLeader) ?? false;

        private const int TrainingAreaZoneId = 504;

        private bool IsGameInSyncableState() => !this.Context.Player.InPeaceZone
                                                && this.Context.Player.ZoneID != TrainingAreaZoneId
                                                && !string.IsNullOrEmpty(this.Context.Player.SessionID)
                                                && this.Context.Player.PlayerParty.Size > 1;

        private void UpdateSyncState(string trigger)
        {
            this.syncService.SetSessionId(this.Context.Player.SessionID);
            if (!IsGameInSyncableState())
            {
                if (this.syncService.SetMode(SyncServiceMode.Idle))
                {
                    Logger.Log($"SyncState: Idle [{trigger}]");
                }
                return;
            }

            if (IsLeader())
            {
                if (this.syncService.SetMode(SyncServiceMode.Push))
                {
                    // if became leader, push all monsters
                    for (var index = 0; index < this.Context.Monsters.Length; index++)
                    {
                        var contextMonster = this.Context.Monsters[index];
                        this.syncService.PushMonster(contextMonster, index);
                    }

                    Logger.Log($"SyncState: Push [{trigger}]");
                }
            }
            else
            {
                if (this.syncService.SetMode(SyncServiceMode.Poll))
                {
                    Logger.Log($"SyncState: Poll [{trigger}]");
                }
            }
        }
        
        
        /// Waiting for <see cref="Game.StartScanning"/> call to finish, so we can be sure that scan loops are started. 
        private async Task WaitForScan()
        {
            while (this.Context != null && !this.Context.IsActive)
            {
                await Task.Delay(500);
            }

            if (this.Context != null)
            {
                OnScanStarted();
            }
        }

        private void OnScanStarted()
        {
            for (var index = 0; index < this.Context.Monsters.Length; index++)
            {
                var monster = this.Context.Monsters[index];
                UpdateMonsterScan(monster, index);
            }

            Logger.Log("Replaced monster polling routine");
        }

        private void UpdateMonsterScan(Monster monster, int index)
        {
            monster.StopThread();
            var action = CreateMonsterScanFn(monster, index);
            var scanRef = new ThreadStart(action);
            var scan = new Thread(scanRef) {Name = $"SyncPlugin_Monster.{monster.MonsterNumber}"};
            ReflectionsHelper.UpdateMonsterScanRefs(monster, scanRef, scan);

            scan.Start();
        }

        private Action CreateMonsterScanFn(Monster monster, int index)
        {
            void MonsterScan()
            {
                while (true)
                {
                    if (!Kernel.GameIsRunning)
                    {
                        Thread.Sleep(1000);
                        continue;
                    }

                    var isLeader = IsLeader();
                    if (isLeader)
                    {
                        this.LeadersMonsterUpdate(monster);
                        if (this.syncService.Mode == SyncServiceMode.Push 
                            && !string.IsNullOrEmpty(monster.Id)
                            && IsGameInSyncableState())
                        {
                            this.syncService.PushMonster(monster, index);
                        }
                    }
                    else
                    {
                        this.PeersMonsterUpdate(monster);
                        if (this.syncService.Mode == SyncServiceMode.Poll 
                            && !string.IsNullOrEmpty(monster.Id)
                            && IsGameInSyncableState())
                        {
                            PullData(monster);
                        }
                    }

                    Thread.Sleep(UserSettings.PlayerConfig.Overlay.GameScanDelay);
                }
                // ReSharper disable once FunctionNeverReturns - endless loop by design. Thread.Abort will be used to stop it
            }

            return MonsterScan;
        }

        private void PullData(Monster monster)
        {
            using var monstersBorrow = this.syncService.BorrowMonsters();
            var monsterModel = monstersBorrow.Value.FirstOrDefault(m => m.Id == monster.Id);
            if (monsterModel == null)
            {
                return;
            }

            UpdateFromMonsterModel(monster, monsterModel);
        }

        private void UpdateFromMonsterModel(Monster monster, MonsterModel monsterModel)
        {
            var parts = monsterModel.Parts;
            if (monster.Parts.Count == 0 || monster.Ailments.Count == 0)
            {
                Logger.Trace("Monster isn't initialized, update skipped");
                return;
            }
            
            if (monster.Parts.Count == parts.Count)
            {
                for (var i = 0; i < parts.Count; i++)
                {
                    monster.Parts[i].SetPartInfo(parts[i].ToDomain());
                    monster.Parts[i].IsRemovable = parts[i].IsRemovable;
                    this.UpdateTenderizePart(monster.Parts[i], parts[i]);
                }
            }
            else
            {
                Logger.Warn($"Cannot find all parts to update! ({monster.Parts.Count} != {parts.Count})");
            }

            if (monster.Ailments.Count == monsterModel.Ailments.Count)
            {
                for (var index = 0; index < monsterModel.Ailments.Count; index++)
                {
                    var ailmentModel = monsterModel.Ailments[index];
                    var ailment = monster.Ailments[index];
                    // TODO: ailments timers should not be synced, since peer member's game keep track of it
                    // this is here because GetMonsterAilments is disabled, so all ailment data must be sourced from somewhere
                    ailment.SetAilmentInfo(ailmentModel.ToDomain());
                }
            }
            else
            {
                Logger.Warn($"Cannot find all ailments to update! ({monster.Ailments.Count} != {monsterModel.Ailments.Count})");
            }
        }
    }
}