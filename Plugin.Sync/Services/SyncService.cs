using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Plugin.Sync.Connectivity;
using Plugin.Sync.Connectivity.Model;
using Plugin.Sync.Model;
using Plugin.Sync.Util;
using Stateless;
using Stateless.Graph;

namespace Plugin.Sync.Services
{
    public enum State
    {
        Idle,
        WaitForSessionId,
        Connecting,
        Polling,
        Pushing,
        Reconnecting,
        RegisterInSession,
        Disconnecting,
        VersionCheck,
        WaitingForPlayers
    }

    public enum Trigger
    {
        // common
        SetPush,
        SetPoll,
        SetIdle,
        SessionIdChanged,
        
        AloneInSession,
        NotAloneInSession,
        
        // VersionCheck state
        VersionOk,
        WrongVersion,
        
        // websockets
        SendingError, // can also be sent by version check
        ConnectionFailed,
        Connected,
        
        // WaitForPlayers state
        PlayerWaitingTimeout,
        
        // Disconnecting state
        Disconnected
    }

    public class SyncService
    {
        private static readonly Version RequiredApiVersion = new Version(0, 1);
        
        private readonly StateMachine<State, Trigger> stateMachine;
        private readonly DomainWebsocketClient websocketClient;
        private CancellationTokenSource currentOperationCts = new CancellationTokenSource();

        private readonly PollService poll;
        private readonly PushService push;
        
        private string sessionId;
        
        private readonly object setModeLocker = new object();

        public string SessionId
        {
            get => this.sessionId;
            set
            {
                if (this.sessionId == value)
                    return;
                this.sessionId = value;
                Logger.Debug($"Using session id '{value}'");
                Fire(Trigger.SessionIdChanged);
            }
        }

        public SyncServiceMode Mode { get; private set; }

        public SyncService()
        {
            // -- websockets
            var msgHandler = new EventMessageHandler();
            msgHandler.OnMessage += HandleMessage;
            this.websocketClient = new DomainWebsocketClient(msgHandler, new JsonMessageEncoder());
            this.websocketClient.OnConnectionError += OnConnectionError;
            
            // -- push/poll
            this.push = new PushService(this.websocketClient);
            this.push.OnSendFailed += PushOnOnSendFailed;
            this.poll = new PollService();
            
            // -- state machine
            this.stateMachine = new StateMachine<State, Trigger>(State.Idle, FiringMode.Queued);
            this.stateMachine.OnTransitioned(OnTransitioned);
            this.stateMachine.OnUnhandledTrigger((state, trigger) =>
            {
                Logger.Debug($"Unhandled trigger: {state} {trigger}");
            });

            InitStateMachine();
        }

        private void InitStateMachine()
        {
            // common transitions
            var selectWorkingState = new TransitionInfo<State>("working mode")
            {
                Fn = () => this.Mode == SyncServiceMode.Poll ? State.Polling : State.Pushing,
                Destinations =
                {
                    {State.Polling, "2+ members & 'poll' mode"},
                    {State.Pushing, "2+ members & 'push' mode"}
                }
            };
            
            // idle
            this.stateMachine.Configure(State.Idle)
                .OnEntry(() => SetMode(SyncServiceMode.Idle))
                .Permit(Trigger.SetPush, State.VersionCheck)
                .Permit(Trigger.SetPoll, State.VersionCheck);
            
            // version check
            this.stateMachine.Configure(State.VersionCheck)
                .OnEntryAsync(CheckVersion)
                .OnExit(CancelCurrentOperation)
                
                .Permit(Trigger.SetIdle, State.Idle)
                .Permit(Trigger.WrongVersion, State.Idle)
                .Permit(Trigger.VersionOk, new TransitionInfo<State>("check session")
                {
                    Fn = () => this.SessionId != null ? State.Connecting : State.WaitForSessionId,
                    Destinations =  {
                        {State.WaitForSessionId, "no session id"},
                        {State.Connecting, "has session id"},
                    }
                })
                .Permit(Trigger.SendingError, State.Idle);
            
            // wait for session id
            this.stateMachine.Configure(State.WaitForSessionId)
                .Permit(Trigger.SessionIdChanged, State.Connecting)
                .Permit(Trigger.SetIdle, State.Idle);
            
            // connecting
            this.stateMachine.Configure(State.Connecting)
                .OnEntryAsync(Connect)
                .OnExit(CancelCurrentOperation)
                
                .Permit(Trigger.Connected, State.RegisterInSession)
                .Permit(Trigger.SetIdle, State.Disconnecting)
                .Permit(Trigger.ConnectionFailed, State.Idle);

            // register in session
            this.stateMachine.Configure(State.RegisterInSession)
                .OnEntryAsync(SendSessionInfo)
                
                .PermitReentry(Trigger.SessionIdChanged)
                .PermitReentry(Trigger.SetPush)
                .PermitReentry(Trigger.SetPoll)
                
                .Permit(Trigger.AloneInSession, State.WaitingForPlayers)
                .Permit(Trigger.NotAloneInSession, selectWorkingState)
                .Permit(Trigger.SendingError, State.Reconnecting)
                .Permit(Trigger.SetIdle, State.Disconnecting);

            // wait for players
            this.stateMachine.Configure(State.WaitingForPlayers)
                .OnEntry(WaitForPlayers)
                .OnExit(CancelCurrentOperation)
                
                .Permit(Trigger.SetIdle, State.Disconnecting)
                .Permit(Trigger.ConnectionFailed, State.Reconnecting)
                .Permit(Trigger.SessionIdChanged, State.RegisterInSession)
                .Permit(Trigger.PlayerWaitingTimeout, State.Disconnecting)
                .Permit(Trigger.NotAloneInSession, selectWorkingState);

            // polling
            this.stateMachine.Configure(State.Polling)
                .OnExit(CancelCurrentOperation)
                .OnExit(this.poll.ClearCache)
                
                .Permit(Trigger.SetPush, State.Pushing)
                .Permit(Trigger.SetIdle, State.Disconnecting)
                .Permit(Trigger.SessionIdChanged, State.RegisterInSession)
                .Permit(Trigger.SendingError, State.Reconnecting)
                .Permit(Trigger.ConnectionFailed, State.Reconnecting)
                .Permit(Trigger.AloneInSession, State.WaitingForPlayers);

            // pushing
            this.stateMachine.Configure(State.Pushing)
                .OnEntry(() => this.push.StartPushLoop(this.sessionId), nameof(PushService.StartPushLoop))
                .OnExit(this.push.StopPushLoop)
                
                .Permit(Trigger.SetPoll, State.Polling)
                .Permit(Trigger.SetIdle, State.Disconnecting)
                .Permit(Trigger.SessionIdChanged, State.RegisterInSession)
                .Permit(Trigger.SendingError, State.Reconnecting)
                .Permit(Trigger.ConnectionFailed, State.Reconnecting)
                .Permit(Trigger.AloneInSession, State.WaitingForPlayers);

            // reconnecting
            this.stateMachine.Configure(State.Reconnecting)
                .OnEntryAsync(Connect)
                .OnExit(CancelCurrentOperation)
                
                .Permit(Trigger.SetIdle, State.Idle)
                .Permit(Trigger.ConnectionFailed, State.Idle)
                .Permit(Trigger.Connected, new TransitionInfo<State>("check session id")
                {
                    Fn = () => this.sessionId != null ? State.RegisterInSession : State.WaitForSessionId,
                    Destinations = 
                    {
                        {State.RegisterInSession, "has session id"},
                        {State.WaitForSessionId, "no session id"},
                    }
                });

            // disconnecting
            this.stateMachine.Configure(State.Disconnecting)
                .OnEntryAsync(Disconnect)
                .OnExit(CancelCurrentOperation)
                
                .Permit(Trigger.Disconnected, State.Idle)
                .Permit(Trigger.SetPoll, State.Connecting)
                .Permit(Trigger.SetPush, State.Connecting);
        }
        
        private void PushOnOnSendFailed(object sender, EventArgs e)
        {
            Fire(Trigger.SendingError);
        }

        private async void OnConnectionError(object sender, Exception e)
        {
            Logger.Warn($"Connection error: {e}");
            // guaranteed to not throw exceptions
            await this.websocketClient.Close();
            Fire(Trigger.ConnectionFailed);
        }

        private void OnTransitioned(StateMachine<State, Trigger>.Transition transition)
        {
            Logger.Log($"{transition.Source} - {transition.Trigger} -> {transition.Destination}");
        }

        public string GetStateMachineGraph() => UmlDotGraph.Format(this.stateMachine.GetInfo());

        /// <summary>
        /// Set sync mode (e.g. direction: push/poll). Returns true if changed.
        /// </summary>
        public bool SetMode(SyncServiceMode mode)
        {
            lock (this.setModeLocker)
            {
                if (this.Mode == mode) return false;
                this.Mode = mode;
            }

            switch (mode)
            {
                case SyncServiceMode.Idle:
                    Fire(Trigger.SetIdle);
                    break;
                case SyncServiceMode.Poll:
                    Fire(Trigger.SetPoll);
                    break;
                case SyncServiceMode.Push:
                    Fire(Trigger.SetPush);
                    break;
            }

            return true;
        }
        
        private Task triggerQueueTask = Task.CompletedTask;

        private void Fire(Trigger trigger)
        {
            lock (this.setModeLocker)
            {
                this.triggerQueueTask =
                    this.triggerQueueTask.ContinueWith(_ =>
                    {
                        return this.stateMachine.FireAsync(trigger);
                    }, TaskScheduler.Default);
            }
        }
        
        public volatile string PlayerName = Guid.NewGuid().ToString();

        public void SetSessionId(string sessionId) => this.SessionId = sessionId;
        
        public void PushMonster(MonsterModel model) => this.push.PushMonster(model);
        
        /// <summary>
        /// Take temporary ownership for cached monsters
        /// </summary>
        public Borrow<List<MonsterModel>> BorrowMonsters() => this.poll.BorrowMonsters();

        private async Task CheckVersion()
        {
            var token = this.currentOperationCts.Token;
            try
            {
                var rsp = await new HttpClient().GetStringAsync($"{ConfigService.Current.ServerUrl}/version");
                token.ThrowIfCancellationRequested();
                var apiVersion = Version.Parse(rsp);
                if (apiVersion.CompareTo(RequiredApiVersion) != 0)
                {
                    Logger.Warn(
                        $"API version mismatch, please update plugin! (api version: {apiVersion}, required: {RequiredApiVersion})");
                    return;
                }
                Logger.Log($"API version is ok ({apiVersion})");
                Fire(Trigger.VersionOk);
            }
            catch (OperationCanceledException)
            {
                Logger.Trace("Cancelled connection");
                // noting to do
            }
            catch (HttpRequestException ex)
            {
                Logger.Warn($"Couldn't connect to server. Polling changes will be disabled. Restart application to try again:\n {ex.GetBaseException().Message}");
                Logger.Debug($"{ex}");
                Fire(Trigger.SendingError);
            }
        }

        private void HandleMessage(object sender, IMessage message)
        {
            switch (message)
            {
                case PushMonstersMessage pushMsg:
                    if (pushMsg.SessionId != this.SessionId || this.Mode != SyncServiceMode.Poll)
                    {
                        break;
                    }

                    this.poll.HandlePushMessage(pushMsg);
                    break;

                case SessionStateMsg sessionMsg:
                    HandleSessionMsg(sessionMsg);
                    break;

                case ServerMsg serverMsg:
                    Logger.Log($"[server msg] {serverMsg.Text}", serverMsg.Level);
                    break;
            }
        }

        private void HandleSessionMsg(SessionStateMsg msg)
        {
            Logger.Info($"Session update: {msg.PlayersCount} player, leader is {(msg.LeaderConnected ? "connected" : "not connected")}");
            Fire(msg.PlayersCount > 1 ? Trigger.NotAloneInSession : Trigger.AloneInSession);
        }

        private async Task SendSessionInfo()
        {
            try
            {
                var isLeader = this.Mode == SyncServiceMode.Push;
                await this.websocketClient.Send(new SetSessionMessage(this.SessionId, isLeader), this.currentOperationCts.Token);
                Logger.Log($"Registered in session {this.SessionId} as {(isLeader ? "leader" : "peer")}");
            }
            catch (OperationCanceledException)
            {
                Logger.Trace("Cancelled send");
                // nothing to do
            }
            catch (Exception ex)
            {
                Logger.Warn($"Error on registering in session: {ex.Message}");
                Fire(Trigger.SendingError);
            }
        }

        private async Task Connect()
        {
            try
            {
                var wsUrl = GetWsUrl();
                Logger.Info($"Connecting to '{wsUrl}'...");
                await this.websocketClient.AssertConnected(wsUrl, this.PlayerName, this.currentOperationCts.Token);
                Fire(Trigger.Connected);
            }
            catch (OperationCanceledException)
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
                    Fire(Trigger.ConnectionFailed);
                }
            }
        }

        private async Task Disconnect()
        {
            try
            {
                if (this.websocketClient.IsConnected)
                {
                    await SendCloseConnection();
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
                Fire(Trigger.Disconnected);
            }
        }

        private async void WaitForPlayers()
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(3), this.currentOperationCts.Token);
                Logger.Info("No player connected in 3 minutes, closing connection.");
                SetMode(SyncServiceMode.Idle);
            }
            catch (OperationCanceledException)
            {
                // nothing to do
            }
        }

        private async Task SendCloseConnection()
        {
            Logger.Debug("Sending leave message");
            try
            {
                await this.websocketClient.Send(new CloseConnectionMessage(), this.currentOperationCts.Token);
            }
            catch (OperationCanceledException)
            {
                // nothing to do
            }
            catch (Exception ex)
            {
                Logger.Debug(ex.ToString());
            }
            Logger.Debug("Sending leave message ok");
        }
        
        private void CancelCurrentOperation()
        {
            Logger.Trace($"{nameof(CancelCurrentOperation)} state: {this.stateMachine.State} [start]");
            if (!this.currentOperationCts.IsCancellationRequested)
            {
                this.currentOperationCts.Cancel();
            }

            this.currentOperationCts = new CancellationTokenSource();
            Logger.Trace($"{nameof(CancelCurrentOperation)} state: {this.stateMachine.State} [end]");
        }
        
        private static string GetWsUrl()
        {
            var serverUrl = ConfigService.Current.ServerUrl;
            if (!serverUrl.StartsWith("http"))
            {
                throw new Exception($"Cannot parse server url: '{serverUrl}'");
            }
            var sb = new StringBuilder("ws");
            
            // removing 'http' part: [http]s://example.com/
            sb.Append(serverUrl, 4, serverUrl.Length - 4);
            // adding '/' if missing
            if (!serverUrl.EndsWith("/")) sb.Append("/");
            sb.Append("connect");
            
            // result: https://example.com -> wss://example.com/connect
            return sb.ToString();
        }
    }
}
