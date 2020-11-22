using System.Collections.Generic;
using System.IO;
using Plugin.Sync.Util;

namespace Plugin.Sync.Model
{
    public static class MessageCodes
    {
        public const string Push = nameof(Push);
        public const string SetSession = nameof(SetSession);
        public const string Close = nameof(Close);
        public const string ServerMsg = nameof(ServerMsg);
        public const string SessionState = nameof(SessionState);
    }
    
    
    public class IMessage
    {
        string Type { get; } 
    }

    public class CloseConnectionMessage : IMessage
    {
        public string Type { get; } = MessageCodes.Close;
    }
    
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

    public class SetSessionMessage : IMessage
    {
        public string SessionId { get; set; }
        public string Type { get; } = MessageCodes.SetSession;
        public bool IsLeader { get; set; }

        public SetSessionMessage(string sessionId, bool isLeader)
        {
            this.SessionId = sessionId;
            this.IsLeader = isLeader;
        }

        public SetSessionMessage()
        {
        }
    }

    public class ServerMsg : IMessage
    {
        public string Type { get; } = MessageCodes.ServerMsg;
        public string Text { get; set; }
        public LogLevel Level { get; set; }
    }

    public class SessionStateMsg : IMessage
    {
        public string Type { get; } = MessageCodes.SessionState;
        public int PlayersCount { get; set; }
        public bool LeaderConnected { get; set; }
    }
}