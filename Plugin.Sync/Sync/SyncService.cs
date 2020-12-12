using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Plugin.Sync.Connectivity;
using Plugin.Sync.Connectivity.Model.Messages;
using Plugin.Sync.Logging;
using Plugin.Sync.Model;
using Plugin.Sync.Poll;
using Plugin.Sync.Push;
using Plugin.Sync.Util;
using Stateless;
using Stateless.Graph;

namespace Plugin.Sync.Sync
{
    public partial class SyncService
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
        
        public volatile string PlayerName;
        private SyncServiceMode mode;
        
        private readonly BufferBlock<QueueItem> triggerQueue = new BufferBlock<QueueItem>();

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
                // no need for cancellation token, since it is okay to not handle this message, since internal state is updated anyway
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
                this.OnSyncModeChanged?.Invoke(this, value);
            }
        }
        public IVersionFetcher VersionFetcher { get; set; }
        private readonly Stopwatch scheduleStopWatch = new Stopwatch();
        
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
        
        private readonly object fireLocker = new object();
        
        private void Fire(Trigger trigger, CancellationToken? cancellationToken = null)
        {
            lock (this.fireLocker)
            {
                if (cancellationToken?.IsCancellationRequested ?? false)
                {
                    Logger.Trace($"Fire {trigger} CANCELLED");
                    return;
                }
                Logger.Trace($"Fire {trigger}");
                this.triggerQueue.Post(new QueueItem(trigger, cancellationToken ?? CancellationToken.None));
            }
        }

        private async Task TriggerQueueLoop(CancellationToken token)
        {
            Logger.Trace($"{nameof(TriggerQueueLoop)} start");
            while (!token.IsCancellationRequested)
            {
                var item = await this.triggerQueue.ReceiveAsync(token);
                var trigger = item.Trigger;
                
                if (Logger.IsEnabled(LogLevel.Trace))
                {
                    Logger.Trace($"Received {item.Trigger} [{this.triggerQueue.Count} in buffer, thread: {Thread.CurrentThread.Name} ({Thread.CurrentThread.ManagedThreadId})] {(item.CancellationToken.IsCancellationRequested ? "CANCELLED" : "")}");
                }

                if (item.CancellationToken.IsCancellationRequested)
                {
                    Logger.Trace($"{trigger} end - cancelled");
                    continue;
                }
                
                await HandleTrigger(trigger);

                Logger.Trace($"{trigger} end");
            }
            Logger.Trace($"{nameof(TriggerQueueLoop)} end");
        }

        private async Task HandleTrigger(Trigger trigger)
        {
            try
            {
                // not a very good solution, but oh well. This should change Mode sequentially before running trigger
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

        private async Task SendSessionInfo(CancellationToken cancellationToken)
        {
            try
            {
                var isLeader = this.Mode == SyncServiceMode.Push;
                await this.websocketClient.Send(new SetSessionMessage(this.SessionId, isLeader), cancellationToken);
                Logger.Log($"Registered in session {this.SessionId} as {(isLeader ? "leader" : "peer")}");
            }
            catch (OperationCanceledException)
            {
                Logger.Trace($"{nameof(OperationCanceledException)} cancelled");
            }
            catch (Exception ex)
            {
                Logger.Warn($"Error on registering in session: {ex.Message}");
                Fire(Trigger.SendingError, cancellationToken);
            }
        }

        private async Task Connect(CancellationToken cancellationToken)
        {
            try
            {
                Logger.Info($"Connecting to '{this.websocketClient.Endpoint}'...");
                var name = ConfigService.TraceName ?? this.PlayerName;
                
                if (await this.websocketClient.AssertConnected(cancellationToken) && !string.IsNullOrEmpty(name))
                {
                    Logger.Debug($"Sending name '{name}'");
                    var msg = new SetNameMessage(name);
                    await this.websocketClient.Send(msg, cancellationToken);
                }

                Fire(Trigger.Connected, cancellationToken);
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
                    Fire(Trigger.ConnectionFailed, cancellationToken);
                }
            }
        }

        private async Task Disconnect(CancellationToken cancellationToken)
        {
            try
            {
                await this.websocketClient.Close();
            }
            catch (Exception ex)
            {
                Logger.Debug(ex.ToString());
            }
            finally
            {
                Fire(Trigger.Disconnected, cancellationToken);
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
    }
}
