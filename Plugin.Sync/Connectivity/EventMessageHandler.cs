using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Plugin.Sync.Connectivity.Model.Messages;

namespace Plugin.Sync.Connectivity
{
    /// <summary>
    /// Consume Websockets inbound messages and publish them as events.
    /// </summary>
    public class EventMessageHandler : BaseMessageHandler, IMessageHandler
    {
        public event EventHandler<IMessage> OnMessage;
        
        public void ReceiveMessage(Stream stream)
        {
            var sr = new StreamReader(stream, Encoding.UTF8);
            using var jsonReader = new JsonTextReader(sr);
            var jObj = JObject.Load(jsonReader);
            var msg = ParseMessage(jObj);
            if (msg != null)
            {
                this.OnMessage?.Invoke(this, msg);
            }
        }
    }
}