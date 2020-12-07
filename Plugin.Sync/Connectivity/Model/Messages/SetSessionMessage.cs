namespace Plugin.Sync.Connectivity.Model
{
    public class SetSessionMessage : IMessage
    {
        public string SessionId { get; set; }
        public string Type { get; } = MessageCodes.SetSession;
        public bool IsLeader { get; set; }

        public SetSessionMessage(string sessionId, bool isLeader)
        {
            this.SessionId = sessionId;
            this.IsLeader = isLeader;
        }

        public SetSessionMessage()
        {
        }
    }
}