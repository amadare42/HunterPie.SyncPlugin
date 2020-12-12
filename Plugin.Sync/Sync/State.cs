namespace Plugin.Sync.Sync
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
}