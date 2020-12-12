namespace Plugin.Sync.Connectivity.Model.Messages
{
    public class LeaveSessionMessage : IMessage
    {
        public string Type { get; } = MessageCodes.LeaveSession;
    }
}