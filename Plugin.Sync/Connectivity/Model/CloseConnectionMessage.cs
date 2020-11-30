namespace Plugin.Sync.Connectivity.Model
{
    public class CloseConnectionMessage : IMessage
    {
        public string Type { get; } = MessageCodes.Close;
    }
}