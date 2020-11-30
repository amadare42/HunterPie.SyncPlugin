using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Plugin.Sync.Connectivity.Model;

namespace Plugin.Sync.Connectivity
{
    /// <summary>
    /// Consume inbound Websocket messages and put them into queue to consume later
    /// </summary>
    public class QueuedMessageHandler : BaseMessageHandler, IMessageHandler
    {
        private object queueLocker = new object();
        private Queue<IMessage> messageQueue = new Queue<IMessage>();
        
        public IEnumerable<IMessage> ConsumeMessages()
        {
            lock (this.queueLocker)
            {
                if (!this.messageQueue.Any())
                {
                    return Enumerable.Empty<IMessage>();
                }

                var messages = this.messageQueue.ToArray();
                this.messageQueue.Clear();
                return messages;
            }
        }
        
        void IMessageHandler.ReceiveMessage(Stream stream)
        {
            var sr = new StreamReader(stream, Encoding.UTF8);
            using var jsonReader = new JsonTextReader(sr);
            var jObj = JObject.Load(jsonReader);
            var msg = ParseMessage(jObj);
            if (msg != null)
            {
                lock (this.queueLocker)
                {
                    this.messageQueue.Enqueue(msg);
                }
            }
        }
    }
}