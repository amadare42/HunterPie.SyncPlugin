using System.Collections.Generic;
using Plugin.Sync.Model;

namespace Plugin.Sync.Connectivity.Model.Messages
{
    public class PushMonstersMessage : IMessage
    {
        public string SessionId { get; set; }

        public string Type { get; } = MessageCodes.Push;
        
        public List<MonsterModel> Data { get; set; }

        public PushMonstersMessage(string sessionId, List<MonsterModel> data)
        {
            this.SessionId = sessionId;
            this.Data = data;
        }

        public PushMonstersMessage()
        {
        }
    }
}