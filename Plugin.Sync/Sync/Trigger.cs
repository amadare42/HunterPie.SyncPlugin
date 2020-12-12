namespace Plugin.Sync.Sync
{
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
}