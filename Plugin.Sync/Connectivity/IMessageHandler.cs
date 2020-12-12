using System.IO;

namespace Plugin.Sync.Connectivity
{
    /// <summary>
    /// Handles inbound Websocket messages.
    /// </summary>
    public interface IMessageHandler
    {
        void ReceiveMessage(Stream stream);
    }
}