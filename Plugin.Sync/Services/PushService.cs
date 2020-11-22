using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Plugin.Sync.Connectivity;
using Plugin.Sync.Model;
using Plugin.Sync.Util;

namespace Plugin.Sync.Services
{
    public class PushService
    {
        static Version ApiVersion = new Version(0, 1);
        
        public string SessionId { get; set; }
        
        /// <summary>
        /// Should be used for queue and cached monster synchronization.
        /// </summary>
        private readonly object locker = new object();
        
        private readonly ConcurrentDictionary<string, MonsterModel> cachedMonsters = new ConcurrentDictionary<string, MonsterModel>();
        private readonly List<MonsterModel> pushQueue = new List<MonsterModel>();
        
        // private readonly SyncServerClient client = new SyncServerClient();
        
        private CancellationTokenSource cancellationTokenSource;
        private Thread thread;
        
        private readonly DiffModelGenerator diffModelGenerator = new DiffModelGenerator();

        public void SetEnabled(bool state)
        {
            var isRunning = this.cancellationTokenSource != null &&
                            !this.cancellationTokenSource.IsCancellationRequested;
            Logger.Trace($"PushService.SetState -> isRunning: {isRunning}, -> {state}");
            // same state, don't do anything
            if (state == isRunning) return;
            if (state)
            {
                if (this.cancellationTokenSource != null && !this.cancellationTokenSource.IsCancellationRequested)
                {
                    this.cancellationTokenSource.Cancel();
                }
                this.cancellationTokenSource = new CancellationTokenSource();
                var scanRef = new ThreadStart(() => PushLoop(this.cancellationTokenSource.Token));
                this.thread = new Thread(scanRef) {Name = "SyncPlugin_PushLoop"};
                ClearData();
                this.thread.Start();
            }
            else
            {
                if (this.cancellationTokenSource != null && !this.cancellationTokenSource.IsCancellationRequested)
                {
                    this.cancellationTokenSource?.Cancel();
                }

                ClearData();
                this.thread?.Join();
            }
            Logger.Trace("PushService.SetState -> changed");
        }

        public void PushMonster(MonsterModel model)
        {
            if (string.IsNullOrEmpty(this.SessionId))
            {
                return;
            }

            if (string.IsNullOrEmpty(model.Id))
            {
                Logger.Trace("Monster id is empty");
                return;
            }

            lock (this.locker)
            {
                if (this.cachedMonsters.TryGetValue(model.Id, out var existingMonster) && existingMonster.Equals(model))
                {
                    return;
                }

                this.cachedMonsters[model.Id] = model;
                this.pushQueue.Add(model);
            }
        }
        
        public async Task<bool> CheckVersion(CancellationToken cancellationToken)
        {
            try
            {
                var rsp = await new HttpClient().GetStringAsync($"{ConfigService.Current.ServerUrl}/version");
                cancellationToken.ThrowIfCancellationRequested();
                var apiVersion = Version.Parse(rsp);
                if (apiVersion.CompareTo(ApiVersion) != 0)
                {
                    Logger.Warn(
                        $"Api version mismatch, please update plugin! (api version: {apiVersion}, required: {ApiVersion})");
                    return false;
                }
                Logger.Log("Api version ok");

                return true;
            }
            catch (TaskCanceledException)
            {
                return false;
            }
            catch (HttpRequestException ex)
            {
                Logger.Warn($"Cannot get API version. Polling changes will not enable. {ex}");
                return false;
            }
        }
        
        
        private static string GetWsUrl() => Regex.Replace(ConfigService.Current.ServerUrl, @"^http", "ws") + "/connect";

        private async void PushLoop(CancellationToken token)
        {
            var sw = new Stopwatch();
            var messageQueue = new QueuedMessageHandler();
            using var client = new DomainWebsocketClient(messageQueue, new JsonMessageEncoder());
            var isReconnecting = false;
            var isSingle = true;
            var serverUrl = GetWsUrl();

            try
            {
                if (!await CheckVersion(token))
                {
                    return;
                }
            }
            catch (TaskCanceledException)
            {
                // nothing to do
            }

            while (true)
            {
                try
                {
                    token.ThrowIfCancellationRequested();
                    
                    // handle messages from server
                    foreach (var message in messageQueue.ConsumeMessages())
                    {
                        switch (message)
                        {
                            case SessionStateMsg session:
                            {
                                Logger.Info($"Session update: {session.PlayersCount} player, leader is {(session.LeaderConnected ? "connected" : "not connected")}");
                                isSingle = session.PlayersCount == 1;
                                break;
                            }

                            case ServerMsg serverMsg:
                            {
                                Logger.Log($"[server msg] {serverMsg.Text}", serverMsg.Level);
                                break;
                            }
                        }
                    }
                
                    // wait for session id
                    if (string.IsNullOrEmpty(this.SessionId))
                    {
                        await Task.Delay(50, token);
                        continue;
                    }

                    // connect and set session id
                    if (await client.AssertConnected(serverUrl, token))
                    {
                        await client.Send(new SetSessionMessage(this.SessionId, true), token);
                    }
                    isReconnecting = false;

                    // don't need to push anything if became solo in session
                    // if (isSingle)
                    // {
                    //     await Task.Delay(50, token);
                    //     continue;
                    // }
                    var monsters = ConsumeQueue();

                    // wait for changes to appear
                    if (!monsters.Any())
                    {
                        await Task.Delay(50, token);
                        continue;
                    }

                    // sending diffs
                    var monsterDiffs = this.diffModelGenerator.GetDiffs(monsters);
                    var dto = new PushMonstersMessage(this.SessionId, monsterDiffs);
                    await client.Send(dto, token);

                    Logger.Trace($"PUSH [{GetTraceData(monsterDiffs)}] ({sw.ElapsedMilliseconds} ms from last push)");
                    sw.Restart();

                    // throttling
                    await Task.Delay(250, token);
                }
                catch (OperationCanceledException)
                {
                    await client.Close();
                    Logger.Debug("Push thread stopped.");
                    return;
                }
                catch (Exception ex)
                {
                    if (!isReconnecting)
                    {
                        Logger.Error($"Error on sending data: {ex}. Trying to reconnect...");
                        isReconnecting = true;
                    }
                    else
                    {
                        Logger.Error("Reconnect failed, push stopped - no monster updates will be available.");
                        await client.Close();
                        throw;
                    }
                }
            }
        }

        /// <summary>
        /// Clear queue and normalize to get only latest monster updates
        /// </summary>
        private List<MonsterModel> ConsumeQueue()
        {
            lock (this.locker)
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

        private void ClearData()
        {
            lock (this.locker)
            {
                this.pushQueue.Clear();
                this.cachedMonsters.Clear();
                this.diffModelGenerator.Clear();
            }
        }
    }
}
