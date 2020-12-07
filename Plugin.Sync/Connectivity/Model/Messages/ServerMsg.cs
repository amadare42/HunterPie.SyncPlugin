using Plugin.Sync.Util;

namespace Plugin.Sync.Connectivity.Model
{
    public class ServerMsg : IMessage
    {
        public string Type { get; } = MessageCodes.ServerMsg;
        public string Text { get; set; }
        public LogLevel Level { get; set; }
    }
}