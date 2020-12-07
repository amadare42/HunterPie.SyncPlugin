namespace Plugin.Sync.Connectivity.Model
{
    public class LeaveSessionMessage : IMessage
    {
        public string Type { get; } = MessageCodes.LeaveSession;
    }
}