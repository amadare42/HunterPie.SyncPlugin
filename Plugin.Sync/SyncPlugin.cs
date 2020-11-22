using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HunterPie.Core;
using HunterPie.Core.Events;
using HunterPie.Memory;
using HunterPie.Plugins;
using Plugin.Sync.Model;
using Plugin.Sync.Services;
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
            else if (this.Context?.Player.PlayerParty.Members.All(m => !m.IsInParty) ?? true)
            {
                Logger.Debug("Zone changed, but party is not loaded yet. Wait for next event.");
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
                    foreach (var monster in this.Context.Monsters)
                    {
                        var monsterModel = MapMonster(monster);
                        this.syncService.PushMonster(monsterModel);
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
            foreach (var monster in this.Context.Monsters)
            {
                UpdateMonsterScan(monster);
            }

            Logger.Log("Replaced monster polling routine");
        }

        private void UpdateMonsterScan(Monster monster)
        {
            ReflectionsHelper.StopMonsterThread(monster);
            var action = CreateMonsterScanFn(monster);
            var scanRef = new ThreadStart(action);
            var scan = new Thread(scanRef) {Name = $"SyncPlugin_Monster.{monster.MonsterNumber}"};
            ReflectionsHelper.UpdateMonsterScanRefs(monster, scanRef, scan);

            scan.Start();
        }

        private Action CreateMonsterScanFn(Monster monster)
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
                            var monsterModel = MapMonster(monster);
                            this.syncService.PushMonster(monsterModel);
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
            if (monsterModel != null)
            {
                UpdateFromMonsterModel(monster, monsterModel);
            }
            
            // we don't need to process same updates again, so we can clear them
            monstersBorrow.Value.Clear();
        }

        private void UpdateFromMonsterModel(Monster monster, MonsterModel monsterModel)
        {
            var parts = monster.Parts;
            var updatedParts = monsterModel.Parts;
            var ailments = monster.Ailments;
            var updatedAilments = monsterModel.Ailments;
            
            if (parts.Count == 0 || ailments.Count == 0)
            {
                Logger.Trace("Monster isn't initialized, update skipped");
                return;
            }
            
            for (var i = 0; i < parts.Count; i++)
            {
                var upd = updatedParts.FirstOrDefault(p => p.Index == i);
                if (upd == null) continue;
                
                parts[i].SetPartInfo(upd.ToDomain());
                monster.Parts[i].IsRemovable = upd.IsRemovable;
                this.UpdateTenderizePart(monster.Parts[i], upd);
            }
            
            for (var i = 0; i < ailments.Count; i++)
            {
                var upd = updatedAilments.FirstOrDefault(p => p.Index == i);
                if (upd == null) continue;
                
                // TODO: ailments timers should not be synced, since peer member's game keep track of it
                // this is here because GetMonsterAilments is disabled, so all ailment data must be sourced from somewhere
                monster.Ailments[i].SetAilmentInfo(upd.ToDomain());
            }
        }
        
        private static MonsterModel MapMonster(Monster monster)
        {
            return new MonsterModel
            {
                Id = monster.Id,
                Parts = monster.Parts.Select(MonsterPartModel.FromDomain).ToList(),
                Ailments = monster.Ailments.Select(AilmentModel.FromDomain).ToList()
            };
        }
    }
}