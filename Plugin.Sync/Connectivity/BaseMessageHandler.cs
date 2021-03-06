﻿using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Plugin.Sync.Connectivity.Model.Messages;
using Plugin.Sync.Logging;
using Plugin.Sync.Util;

namespace Plugin.Sync.Connectivity
{
    public class BaseMessageHandler
    {
        protected const int MaxMessageSize = 16384;
        protected readonly JsonSerializer Serializer;

        protected BaseMessageHandler()
        {
            this.Serializer = CreateSerializer();
        }

        public static JsonSerializer CreateSerializer()
        {
            return new JsonSerializer
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver(),
                Converters =
                {
                    // we don't need that level of precision, this will reduce packet size a bit
                    new LimitFloatPrecisionConverter(2),
                    new Newtonsoft.Json.Converters.StringEnumConverter(new CamelCaseNamingStrategy()),
                    new JsonArrayObjectConverter()
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
                if (this.inboundMessages.TryGetValue(type.ToLower(), out var msgType))
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
        
        private readonly Dictionary<string, Type> inboundMessages = new Dictionary<string, Type>
        {
            { MessageCodes.Push.ToLower(), typeof(PushMonstersMessage) },
            { MessageCodes.ServerMsg.ToLower(), typeof(ServerMsg) },
            { MessageCodes.SessionState.ToLower(), typeof(SessionStateMsg) },
        };

        private IMessage Parse(JObject jObj, Type type) => (IMessage) this.Serializer.Deserialize(new JTokenReader(jObj), type);
    }
}