using System;
using System.Threading;
using System.Threading.Tasks;
using Plugin.Sync.Push;
using Plugin.Sync.Util;

namespace Plugin.Sync.Sync
{
    public partial class SyncService
    {
        private readonly OperationScheduler operationScheduler = new OperationScheduler();
        
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
                .OnEntry(() =>
                {
                    // in case any other async operation wasn't finished yet. Probably wont be needed.
                    CancelCurrentAction();
                    
                    // this is needed, so if we got there from any other transition other than calling SetMode,
                    // Mode should still be updated
                    this.Mode = SyncServiceMode.Idle;
                })
                
                .Permit(Trigger.SetPush, State.VersionCheck)
                .Permit(Trigger.SetPoll, State.VersionCheck);
            
            // version check
            this.stateMachine.Configure(State.VersionCheck)
                .OnEntryAsync(Cancellable(CheckVersion))
                .OnExit(CancelCurrentAction)
                
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
                .OnEntryAsync(Cancellable(Connect))
                .OnExit(CancelCurrentAction)
                
                .Permit(Trigger.Connected, State.RegisterInSession)
                .Permit(Trigger.SetIdle, State.Disconnecting)
                .Permit(Trigger.ConnectionFailed, State.Idle);

            // register in session
            this.stateMachine.Configure(State.RegisterInSession)
                .OnEntryAsync(Cancellable(SendSessionInfo))
                .OnExit(CancelCurrentAction)
                
                .PermitReentry(Trigger.SessionIdChanged)
                .PermitReentry(Trigger.SetPush)
                .PermitReentry(Trigger.SetPoll)
                
                .Permit(Trigger.AloneInSession, State.WaitingForPlayers)
                .Permit(Trigger.NotAloneInSession, selectWorkingState)
                .Permit(Trigger.SendingError, State.Reconnecting)
                .Permit(Trigger.SetIdle, State.Disconnecting);

            // wait for players
            this.stateMachine.Configure(State.WaitingForPlayers)
                .OnEntryAsync(Cancellable(WaitForPlayers))
                .OnExit(CancelCurrentAction)
                
                .Permit(Trigger.SetIdle, State.Disconnecting)
                .Permit(Trigger.ConnectionFailed, State.Reconnecting)
                .Permit(Trigger.SessionIdChanged, State.RegisterInSession)
                .Permit(Trigger.PlayerWaitingTimeout, State.Disconnecting)
                .Permit(Trigger.NotAloneInSession, selectWorkingState);

            // polling
            this.stateMachine.Configure(State.Polling)
                .OnEntry(() => this.Mode = SyncServiceMode.Poll)
                .OnExit(this.poll.ClearCache)
                
                .Permit(Trigger.SetPush, State.Pushing)
                .Permit(Trigger.SetIdle, State.Disconnecting)
                .Permit(Trigger.SessionIdChanged, State.RegisterInSession)
                .Permit(Trigger.SendingError, State.Reconnecting)
                .Permit(Trigger.ConnectionFailed, State.Reconnecting)
                .Permit(Trigger.AloneInSession, State.WaitingForPlayers);

            // pushing
            this.stateMachine.Configure(State.Pushing)
                .OnEntry(() =>
                {
                    this.push.StartPushLoop(this.sessionId);
                    this.Mode = SyncServiceMode.Push;
                }, nameof(PushService.StartPushLoop))
                .OnExit(this.push.StopPushLoop)
                
                .Permit(Trigger.SetPoll, State.Polling)
                .Permit(Trigger.SetIdle, State.Disconnecting)
                .Permit(Trigger.SessionIdChanged, State.RegisterInSession)
                .Permit(Trigger.SendingError, State.Reconnecting)
                .Permit(Trigger.ConnectionFailed, State.Reconnecting)
                .Permit(Trigger.AloneInSession, State.WaitingForPlayers);

            // reconnecting
            this.stateMachine.Configure(State.Reconnecting)
                .OnEntryAsync(Cancellable(Connect))
                
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
                .OnEntryAsync(async () =>
                {
                    CancelCurrentAction();
                    await Disconnect(CancellationToken.None);
                }, nameof(Disconnect))
                
                .Permit(Trigger.Disconnected, State.Idle)
                .Permit(Trigger.SetPoll, State.Connecting)
                .Permit(Trigger.SetPush, State.Connecting);
        }
        
        /// <summary>
        /// Start operation in current execution context, but continue in scheduled queue.
        /// This operation can be cancelled.
        /// </summary>
        private Func<Task> Cancellable(Func<CancellationToken, Task> action) => this.operationScheduler.CreateAction(action);
        
        /// <summary>
        /// Start operation in current execution context, but continue in scheduled queue.
        /// This operation can be cancelled.
        /// </summary>
        private Func<Task> Cancellable(Action action) => this.operationScheduler.CreateAction(action);

        /// <summary>
        /// Request canceling currently executing scheduled operation.
        /// </summary>
        private void CancelCurrentAction() => this.operationScheduler.CancelCurrentAction();
    }
}