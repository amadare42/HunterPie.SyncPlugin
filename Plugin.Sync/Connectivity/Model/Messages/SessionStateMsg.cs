namespace Plugin.Sync.Connectivity.Model.Messages
{
    public class SessionStateMsg : IMessage
    {
        public string Type { get; } = MessageCodes.SessionState;
        public int PlayersCount { get; set; }
        public bool LeaderConnected { get; set; }
    }
}