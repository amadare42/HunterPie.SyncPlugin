using Plugin.Sync.Logging;

namespace Plugin.Sync.Connectivity.Model.Messages
{
    public class ServerMsg : IMessage
    {
        public string Type { get; } = MessageCodes.ServerMsg;
        public string Text { get; set; }
        public LogLevel Level { get; set; }
    }
}