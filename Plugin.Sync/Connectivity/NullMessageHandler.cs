using System.IO;

namespace Plugin.Sync.Connectivity
{
    /// <summary>
    /// Consumes inbound messages without doing anything with them.
    /// </summary>
    public class NullMessageHandler : IMessageHandler
    {
        public void ReceiveMessage(Stream stream)
        {
            // don't need to do anything
        }
    }
}