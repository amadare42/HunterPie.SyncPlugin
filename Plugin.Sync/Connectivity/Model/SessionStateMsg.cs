namespace Plugin.Sync.Connectivity.Model
{
    public class SessionStateMsg : IMessage
    {
        public string Type { get; } = MessageCodes.SessionState;
        public int PlayersCount { get; set; }
        public bool LeaderConnected { get; set; }
    }
}