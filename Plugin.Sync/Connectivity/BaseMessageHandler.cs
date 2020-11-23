using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Plugin.Sync.Model;
using Plugin.Sync.Util;

namespace Plugin.Sync.Connectivity
{
    public class BaseMessageHandler
    {
        protected const int MaxMessageSize = 16384;
        protected JsonSerializer Serializer;
        
        public BaseMessageHandler()
        {
            this.Serializer = new JsonSerializer
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver(),
                Converters = 
                {
                    // we don't need that level of precision
                    new LimitFloatPrecisionConverter(2),
                    new Newtonsoft.Json.Converters.StringEnumConverter(new CamelCaseNamingStrategy())
                }
            };
        }
        
        protected IMessage ParseMessage(JObject jObj)
        {
            var type = (string) jObj.SelectToken("type");
            
            if (type == null)
            {
                Logger.Warn("Invalid message: received message doesn't have type.");
                Logger.Debug(jObj.ToString());
                return null;
            }
            
            try
            {
                if (this.messageMap.TryGetValue(type.ToLower(), out var msgType))
                {
                    return Parse(jObj, msgType);
                }
                
                Logger.Warn($"Invalid message: received message have unknown type: {type}.");
                Logger.Debug(jObj.ToString());
                return null;
            }
            catch (Exception ex)
            {
                Logger.Warn($"Error during message parse: {ex.Message}");
                Logger.Debug(jObj.ToString());
                return null;
            }
        }
        
        private readonly Dictionary<string, Type> messageMap = new Dictionary<string, Type>
        {
            { MessageCodes.Push.ToLower(), typeof(PushMonstersMessage) },
            { MessageCodes.ServerMsg.ToLower(), typeof(ServerMsg) },
            { MessageCodes.SessionState.ToLower(), typeof(SessionStateMsg) },
        };

        private IMessage Parse(JObject jObj, Type type) => (IMessage) this.Serializer.Deserialize(new JTokenReader(jObj), type);
    }
}