using System;
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
using Stateless;
using Stateless.Graph;
using Stateless.Reflection;

namespace Plugin.Sync.Services
{
    public enum State
    {
        Idle,
        WaitForSessionId,
        Connecting,
        Working,
        Reconnecting,
        SendSessionId,
        Disconnecting,
        VersionCheck
    }

    public enum Trigger
    {
        Enable,
        Disable,
        Connected,
        SessionIdChanged,
        Subscribed,
        SendingError,
        ConnectionFailed,
        Disconnected,
        WrongVersion,
        VersionOk
    }
    
    public class PollService
    {
        static Version ApiVersion = new Version(0, 1);
        
        private StateMachine<State, Trigger> stateMachine;
        private DomainWebsocketClient websocketClient;
        private Stopwatch stopwatch = new Stopwatch();
        private CancellationTokenSource cts = new CancellationTokenSource();
        
        /// <summary>
        /// Should be used for polled monster synchronization.
        /// </summary>
        private readonly SemaphoreSlim semaphore = new SemaphoreSlim(1);
        private readonly List<MonsterModel> polledMonsters = new List<MonsterModel>();
        private bool isEnabled;
        
        private string sessionId;
        public string SessionId
        {
            get => this.sessionId;
            set
            {
                if (this.sessionId == value)
                    return;
                this.sessionId = value;
                if (!this.stateMachine.IsInState(State.Idle))
                {
                    Task.Run(() => this.stateMachine.FireAsync(Trigger.SessionIdChanged));
                }
            }
        }

        public PollService()
        {
            var msgHandler = new EventMessageHandler();
            this.websocketClient = new DomainWebsocketClient(msgHandler, new JsonMessageEncoder());
            msgHandler.OnMessage += OnMessage;
            this.websocketClient.OnConnectionError += OnConnectionError;
            
            this.stateMachine = new StateMachine<State, Trigger>(State.Idle, FiringMode.Queued);

            // this is needed only for generating correct .dot file for fancy graph
            var dynamicConnect = new DynamicStateInfos
            {
                {State.Connecting, "has session id"},
                {State.WaitForSessionId, "no session id"},
            };
            
            State ConnectOrWaitForSession() => this.SessionId != null ? State.Connecting : State.WaitForSessionId;
            
            this.stateMachine.Configure(State.Idle)
                .Permit(Trigger.Enable, State.VersionCheck);

            this.stateMachine.Configure(State.WaitForSessionId)
                .Permit(Trigger.SessionIdChanged, State.Connecting)
                .Permit(Trigger.Disable, State.Idle);
            
            this.stateMachine.Configure(State.VersionCheck)
                .OnEntryAsync(CheckVersion)
                .OnExit(CancelCurrentOperation)
                .Permit(Trigger.Disable, State.Idle)
                .Permit(Trigger.WrongVersion, State.Idle)
                .PermitDynamic(Trigger.VersionOk, ConnectOrWaitForSession, null, dynamicConnect)
                .Permit(Trigger.SendingError, State.Idle);

            this.stateMachine.Configure(State.Connecting)
                .OnEntryAsync(Connect)
                .OnExit(CancelCurrentOperation)
                .Permit(Trigger.Connected, State.SendSessionId)
                .Permit(Trigger.Disable, State.Disconnecting)
                .Permit(Trigger.ConnectionFailed, State.Idle);

            this.stateMachine.Configure(State.SendSessionId)
                .OnEntryAsync(SendSessionId)
                .OnExit(CancelCurrentOperation)
                .PermitReentry(Trigger.SessionIdChanged)
                .Permit(Trigger.Subscribed, State.Working)
                .Permit(Trigger.Disable, State.Disconnecting);

            this.stateMachine.Configure(State.Working)
                .OnExit(CancelCurrentOperation)
                .OnExit(ClearCache)
                .Permit(Trigger.SessionIdChanged, State.SendSessionId)
                .Permit(Trigger.SendingError, State.Reconnecting)
                .Permit(Trigger.ConnectionFailed, State.Reconnecting)
                .Permit(Trigger.Disable, State.Disconnecting);

            this.stateMachine.Configure(State.Reconnecting)
                .OnEntryAsync(Connect)
                .OnExit(CancelCurrentOperation)
                .Permit(Trigger.Disable, State.Idle)
                .Permit(Trigger.ConnectionFailed, State.Idle)
                .PermitDynamic(Trigger.Connected, ConnectOrWaitForSession, null, dynamicConnect);

            this.stateMachine.Configure(State.Disconnecting)
                .OnEntryAsync(Disconnect)
                .OnExit(CancelCurrentOperation)
                .Permit(Trigger.Disconnected, State.Idle)
                .Permit(Trigger.Enable, State.Connecting);
            
            this.stateMachine.OnTransitioned(OnTransitioned);
        }

        private async void OnConnectionError(object sender, Exception e)
        {
            Logger.Warn($"Connection error: {e}");
            await this.websocketClient.Close();
            await this.stateMachine.FireAsync(Trigger.ConnectionFailed);
        }

        private void OnTransitioned(StateMachine<State, Trigger>.Transition transition)
        {
            Logger.Log($"Polling: [{transition.Trigger}] {transition.Source} -> {transition.Destination}");
        }

        public string GetStateMachineGraph() => UmlDotGraph.Format(this.stateMachine.GetInfo());

        public void SetEnabled(bool enabled)
        {
            if (this.isEnabled == enabled) return;
            this.isEnabled = enabled;
            this.stateMachine.FireAsync(enabled ? Trigger.Enable : Trigger.Disable);
        }

        public async Task CheckVersion()
        {
            var token = this.cts.Token;
            try
            {
                var rsp = await new HttpClient().GetStringAsync($"{ConfigService.Current.ServerUrl}/version");
                token.ThrowIfCancellationRequested();
                var apiVersion = Version.Parse(rsp);
                if (apiVersion.CompareTo(ApiVersion) != 0)
                {
                    Logger.Warn(
                        $"API version mismatch, please update plugin! (api version: {apiVersion}, required: {ApiVersion})");
                    await this.stateMachine.FireAsync(Trigger.WrongVersion);
                    return;
                }
                Logger.Log($"API version is ok ({apiVersion})");
                
                await this.stateMachine.FireAsync(Trigger.VersionOk);
            }
            catch (TaskCanceledException)
            {
                // noting to do
            }
            catch (HttpRequestException ex)
            {
                Logger.Warn($"Couldn't connect to server. Polling changes will be disabled. Restart application to try again:\n {ex.GetBaseException().Message}");
                Logger.Debug($"{ex}");
                await this.stateMachine.FireAsync(Trigger.SendingError);
            }
        }
        
        private void CancelCurrentOperation()
        {
            if (!this.cts.IsCancellationRequested)
            {
                this.cts.Cancel();
            }

            this.cts = new CancellationTokenSource();
        }

        private void OnMessage(object sender, IMessage message)
        {
            switch (message)
            {
                case PushMonstersMessage push:
                    if (push.SessionId != this.SessionId)
                    {
                        break;
                    }
                    var changedMonsters = push.Data;
                    Logger.Trace($"Monsters updated: {changedMonsters.Count} ({this.stopwatch.ElapsedMilliseconds} ms after last update)");
                    UpdateMonsters(changedMonsters);
                    this.stopwatch.Restart();
                    break;

                case SessionStateMsg session:
                {
                    Logger.Info($"Session update: {session.PlayersCount} player, leader is {(session.LeaderConnected ? "connected" : "not connected")}");
                    break;
                }
                
                case ServerMsg serverMsg:
                    Logger.Log($"[server msg] {serverMsg.Text}", serverMsg.Level);
                    break;
            }
        }

        private async Task SendSessionId()
        {
            try
            {
                await this.websocketClient.Send(new SetSessionMessage(this.SessionId, false), this.cts.Token);
                Logger.Log($"Subscribed for updates for session {this.SessionId}");
                await this.stateMachine.FireAsync(Trigger.Subscribed);
            }
            catch (OperationCanceledException)
            {
                // nothing to do
            }
            catch (Exception ex)
            {
                Logger.Warn($"Error on subscribing: {ex.Message}");
                await this.stateMachine.FireAsync(Trigger.SendingError);
            }
        }

        private static string GetWsUrl() => Regex.Replace(ConfigService.Current.ServerUrl, @"^http", "ws") + "/connect";

        private async Task Connect()
        {
            try
            {
                await this.websocketClient.AssertConnected(GetWsUrl(), this.cts.Token);
                await this.stateMachine.FireAsync(Trigger.Connected);
            }
            catch (TaskCanceledException)
            {
                // nothing to do
            }
            catch (Exception ex)
            {
                try
                {
                    Logger.Error($"Connection failed - no monster updates will be available. {ex.Message}");
                    await this.websocketClient.Close();
                }
                finally
                {
                    await this.stateMachine.FireAsync(Trigger.ConnectionFailed);
                }
            }
        }

        private void ClearCache()
        {
            this.semaphore.Wait();
            this.polledMonsters.Clear();
            this.semaphore.Release();
        }

        private async Task Disconnect()
        {
            try
            {
                try
                {
                    if (this.websocketClient.IsConnected)
                    {
                        await this.websocketClient.Send(new CloseConnectionMessage(), this.cts.Token);
                    }
                }
                catch (OperationCanceledException)
                {
                    // nothing to do
                }
                catch (Exception ex)
                {
                    Logger.Debug(ex.ToString());
                }
                
                await this.websocketClient.Close();
            }
            catch (OperationCanceledException)
            {
                // nothing to do
            }
            catch (Exception ex)
            {
                Logger.Debug(ex.ToString());
            }
            finally
            {
                await this.stateMachine.FireAsync(Trigger.Disconnected);
            }
        }

        public Borrow<List<MonsterModel>> BorrowMonsters()
        {
            this.semaphore.Wait();
            return new Borrow<List<MonsterModel>>(this.polledMonsters, ReleaseSemaphore);
        }

        private void ReleaseSemaphore() => this.semaphore.Release();
        
        private void UpdateMonsters(List<MonsterModel> monsters)
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
    }
}
