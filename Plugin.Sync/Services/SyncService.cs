using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
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
        public static readonly Version RequiredApiVersion = new Version(0, 1);
        
        private static readonly IClassLogger Logger = LogManager.GetCurrentClassLogger();

        private readonly StateMachine<State, Trigger> stateMachine;
        private readonly IDomainWebsocketClient websocketClient;

        private readonly PollService poll;
        private readonly PushService push;

        private string sessionId;

        private readonly object triggerLoopTaskLocker = new object();
        private Task triggerLoopTask;
        private readonly SemaphoreSlim connectSemaphore = new SemaphoreSlim(1);
        
        public volatile string PlayerName;
        private SyncServiceMode mode;
        
        private readonly BufferBlock<Trigger> triggerQueue = new BufferBlock<Trigger>();
        
        private readonly OperationScheduler scheduler = new OperationScheduler();

        public SyncService(IDomainWebsocketClient client)
        {
            // -- websockets
            this.websocketClient = client;
            this.websocketClient.OnMessage += HandleMessage;
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
                Logger.Debug($"Unhandled trigger: {trigger} [{state}]");
            });

            this.VersionFetcher = new HttpVersionFetcher();

            InitStateMachine();
            this.scheduleStopWatch.Start();
        }

        public event EventHandler<SyncServiceMode> OnSyncModeChanged;

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

        public SyncServiceMode Mode
        {
            get => this.mode;
            private set
            {
                if (this.mode == value) return;
                this.mode = value;
                OnSyncModeChanged?.Invoke(this, value);
            }
        }
        public IVersionFetcher VersionFetcher { get; set; }
        private readonly Stopwatch scheduleStopWatch = new Stopwatch();

        private Func<Task> Sheduled(Action action) => this.scheduler.CreateAction(action);
        private Func<Task> Sheduled(Func<CancellationToken, Task> action) => this.scheduler.CreateAction(action);
        
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
                .OnEntryAsync(Sheduled(() => this.Mode = SyncServiceMode.Idle))
                .Permit(Trigger.SetPush, State.VersionCheck)
                .Permit(Trigger.SetPoll, State.VersionCheck);
            
            // version check
            this.stateMachine.Configure(State.VersionCheck)
                .OnEntryAsync(Sheduled(CheckVersion))
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
                .OnEntryAsync(Sheduled(Connect))
                .OnExit(CancelCurrentOperation)
                
                .Permit(Trigger.Connected, State.RegisterInSession)
                .Permit(Trigger.SetIdle, State.Disconnecting)
                .Permit(Trigger.ConnectionFailed, State.Idle);

            // register in session
            this.stateMachine.Configure(State.RegisterInSession)
                .OnEntryAsync(Sheduled(SendSessionInfo))
                
                .PermitReentry(Trigger.SessionIdChanged)
                .PermitReentry(Trigger.SetPush)
                .PermitReentry(Trigger.SetPoll)
                
                .Permit(Trigger.AloneInSession, State.WaitingForPlayers)
                .Permit(Trigger.NotAloneInSession, selectWorkingState)
                .Permit(Trigger.SendingError, State.Reconnecting)
                .Permit(Trigger.SetIdle, State.Disconnecting);

            // wait for players
            this.stateMachine.Configure(State.WaitingForPlayers)
                .OnEntryAsync(Sheduled(WaitForPlayers))
                .OnExit(CancelCurrentOperation)
                
                .Permit(Trigger.SetIdle, State.Disconnecting)
                .Permit(Trigger.ConnectionFailed, State.Reconnecting)
                .Permit(Trigger.SessionIdChanged, State.RegisterInSession)
                .Permit(Trigger.PlayerWaitingTimeout, State.Disconnecting)
                .Permit(Trigger.NotAloneInSession, selectWorkingState);

            // polling
            this.stateMachine.Configure(State.Polling)
                .OnEntryAsync(Sheduled(() => this.Mode = SyncServiceMode.Poll))
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
                .OnEntryAsync(Sheduled(() =>
                {
                    this.push.StartPushLoop(this.sessionId);
                    this.Mode = SyncServiceMode.Push;
                }), nameof(PushService.StartPushLoop))
                .OnExit(this.push.StopPushLoop)
                
                .Permit(Trigger.SetPoll, State.Polling)
                .Permit(Trigger.SetIdle, State.Disconnecting)
                .Permit(Trigger.SessionIdChanged, State.RegisterInSession)
                .Permit(Trigger.SendingError, State.Reconnecting)
                .Permit(Trigger.ConnectionFailed, State.Reconnecting)
                .Permit(Trigger.AloneInSession, State.WaitingForPlayers);

            // reconnecting
            this.stateMachine.Configure(State.Reconnecting)
                .OnEntryAsync(Sheduled(Connect))
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
                .OnEntryAsync(Sheduled(Disconnect))
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

        private static void OnTransitioned(StateMachine<State, Trigger>.Transition transition)
        {
            Logger.Log($"{transition.Source} - {transition.Trigger} -> {transition.Destination}");
        }

        #if DEBUG
        public string GetStateMachineGraph() => UmlDotGraph.Format(this.stateMachine.GetInfo());
        #endif

        /// <summary>
        /// Set sync mode (e.g. direction: push/poll). Returns true if changed.
        /// </summary>
        public void SetMode(SyncServiceMode mode)
        {
            EnsureTriggerLoop();
            var trigger = MapModeToTrigger(mode);
            Fire(trigger);
        }

        private static Trigger MapModeToTrigger(SyncServiceMode mode) => mode switch
        {
            SyncServiceMode.Idle => Trigger.SetIdle,
            SyncServiceMode.Poll => Trigger.SetPoll,
            SyncServiceMode.Push => Trigger.SetPush,
            _ => throw new Exception("Unknown mode " + mode)
        };

        private static SyncServiceMode? MapTriggerToMode(Trigger mode) => mode switch
        {
            Trigger.SetIdle => SyncServiceMode.Idle,
            Trigger.SetPoll => SyncServiceMode.Poll,
            Trigger.SetPush => SyncServiceMode.Push,
            _ => null
        };

        private void EnsureTriggerLoop()
        {
            if (this.triggerLoopTask == null || this.triggerLoopTask.IsCompleted)
            {
                lock (this.triggerLoopTaskLocker)
                {
                    if (this.triggerLoopTask == null || this.triggerLoopTask.IsCompleted)
                    {
                        Logger.Trace($"LOOP RQ");
                        this.triggerLoopTask = TriggerQueueLoop(CancellationToken.None);
                    }
                }
            }
        }
        
        private object messageLocker = new object();
        
        private void Fire(Trigger trigger, CancellationToken? token = null)
        {
            lock (this.messageLocker)
            {
                if (token?.IsCancellationRequested ?? false) return;
                Logger.Trace($"Fire {trigger}");
                this.triggerQueue.Post(trigger);
            }
        }
        
        private void CancelCurrentOperation()
        {
            lock (this.messageLocker)
            {
                this.scheduler.CancelCurrentAction();
            }
        }

        private async Task TriggerQueueLoop(CancellationToken token)
        {
            Logger.Trace($"{nameof(TriggerQueueLoop)} start");
            while (!token.IsCancellationRequested)
            {
                var trigger = await this.triggerQueue.ReceiveAsync(token);
                
                if (Logger.IsEnabled(LogLevel.Trace))
                {
                    Logger.Trace($"Received {trigger} [{this.triggerQueue.Count} in buffer, thread: {Thread.CurrentThread.Name} ({Thread.CurrentThread.ManagedThreadId})]");
                }

                try
                {
                    // not a very good solution. This should change Mode sequentially before running trigger
                    // so all Set*Mode triggers implicitly contains two actions: 1) set Mode, 2) change state
                    // therefore if state transition didn't occur, Mode is still changed
                    var nextMode = MapTriggerToMode(trigger);
                    if (nextMode != null)
                    {
                        this.Mode = (SyncServiceMode) nextMode;
                    }
                    
                    await this.stateMachine.FireAsync(trigger);
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error on handling trigger {trigger} [{this.stateMachine.State}]: {ex}");
                }
                Logger.Trace($"{trigger} end");
            }
            Logger.Trace($"{nameof(TriggerQueueLoop)} end");
        }
        
        public void SetSessionId(string sessionId) => this.SessionId = sessionId;

        public void PushMonster(MonsterModel model) => this.push.PushMonster(model);

        /// <summary>
        /// Take temporary ownership for cached monsters
        /// </summary>
        public Borrow<List<MonsterModel>> BorrowMonsters() => this.poll.BorrowMonsters();

        private async Task CheckVersion(CancellationToken token)
        {
            try
            {
                var apiVersion = await this.VersionFetcher.FetchVersion(token);
                if (apiVersion.CompareTo(RequiredApiVersion) != 0)
                {
                    Logger.Warn(
                        $"API version mismatch, please update plugin! (api version: {apiVersion}, required: {RequiredApiVersion})");
                    return;
                }
                Logger.Log($"API version is ok ({apiVersion})");
                Fire(Trigger.VersionOk, token);
            }
            catch (OperationCanceledException)
            {
                Logger.Trace($"{nameof(CheckVersion)} cancelled");
            }
            catch (HttpRequestException ex)
            {
                Logger.Warn($"Couldn't connect to server. Polling changes will be disabled. Restart application to try again:\n {ex.GetBaseException().Message}");
                Logger.Debug($"{ex}");
                Fire(Trigger.SendingError, token);
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

        private async Task SendSessionInfo(CancellationToken token)
        {
            try
            {
                var isLeader = this.Mode == SyncServiceMode.Push;
                await this.websocketClient.Send(new SetSessionMessage(this.SessionId, isLeader), token);
                Logger.Log($"Registered in session {this.SessionId} as {(isLeader ? "leader" : "peer")}");
            }
            catch (OperationCanceledException)
            {
                Logger.Trace($"{nameof(OperationCanceledException)} cancelled");
            }
            catch (Exception ex)
            {
                Logger.Warn($"Error on registering in session: {ex.Message}");
                Fire(Trigger.SendingError, token);
            }
        }

        private async Task Connect(CancellationToken token)
        {
            try
            {
                Logger.Info($"Connecting to '{this.websocketClient.Endpoint}'...");
                var name = ConfigService.TraceName ?? this.PlayerName;
                
                // websocket client is not thread-safe, need to make sure only one connect is active at the time 
                await this.connectSemaphore.WaitAsync(token);
                if (await this.websocketClient.AssertConnected(token) && !string.IsNullOrEmpty(name))
                {
                    Logger.Debug($"Sending name '{name}'");
                    var msg = new SetNameMessage(name);
                    await this.websocketClient.Send(msg, token);
                }

                Fire(Trigger.Connected, token);
            }
            catch (OperationCanceledException)
            {
                Logger.Trace($"{nameof(Connect)} cancelled");
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
                    Fire(Trigger.ConnectionFailed, token);
                }
            }
            finally
            {
                this.connectSemaphore.Release();
            }
        }

        private async Task Disconnect(CancellationToken token)
        {
            try
            {
                if (this.websocketClient.IsConnected)
                {
                    await SendCloseConnection(token);
                }

                await this.websocketClient.Close();
            }
            catch (OperationCanceledException)
            {
                Logger.Trace($"{nameof(Disconnect)} cancelled");
            }
            catch (Exception ex)
            {
                Logger.Debug(ex.ToString());
            }
            finally
            {
                Fire(Trigger.Disconnected, token);
            }
        }

        private async Task WaitForPlayers(CancellationToken token)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(3), token);
                Logger.Info("No player connected in 3 minutes, closing connection.");
                SetMode(SyncServiceMode.Idle);
            }
            catch (OperationCanceledException)
            {
                Logger.Trace($"{nameof(WaitForPlayers)} cancelled");
            }
        }

        private async Task SendCloseConnection(CancellationToken token)
        {
            Logger.Debug("Sending leave message");
            try
            {
                await this.websocketClient.Send(new LeaveSessionMessage(), token);
            }
            catch (OperationCanceledException)
            {
                Logger.Trace($"{nameof(SendCloseConnection)} cancelled");
            }
            catch (Exception ex)
            {
                Logger.Debug(ex.ToString());
            }
            Logger.Debug("Sending leave message ok");
        }

    }
}
