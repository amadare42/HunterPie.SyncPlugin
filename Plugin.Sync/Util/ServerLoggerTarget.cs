using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks.Dataflow;
using Newtonsoft.Json.Linq;

namespace Plugin.Sync.Util
{
    public class ServerLoggerTarget : ILoggerTarget
    {
        private readonly string user;
        private readonly string room;
        private readonly HttpClient client = new HttpClient();
        
        private readonly BufferBlock<JObject> logs = new BufferBlock<JObject>();

        public ServerLoggerTarget(string user, string room)
        {
            this.user = user;
            this.room = room;
            PushLogsLoop();
        }

        public void Log(string message, LogLevel level)
        {
            var jObj = new JObject
            {
                ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ["level"] = level.ToString("G"),
                ["msg"] = message,
                ["text"] = $"{DateTime.Now:HH:mm:ss:fff} [{level:G}] {message}",
                ["user"] = this.user,
                ["room"] = this.room
            };
            this.logs.Post(jObj);
        }

        private async void PushLogsLoop()
        {
            while (true)
            {
                await this.logs.OutputAvailableAsync();
                if (!this.logs.TryReceiveAll(out var entries)) continue;
                
                var arr = new JArray();
                foreach (var jObject in entries)
                {
                    arr.Add(jObject);
                }

                ShipLogs(arr);
            }
        }

        private async void ShipLogs(JArray entry)
        {
            var content = new StringContent(entry.ToString(), Encoding.UTF8, "application/json");
            try
            {
                await this.client.PostAsync(ConfigService.Current.ServerUrl + "/logs/add", content);
            }
            catch (Exception)
            {
                // ignored
            }
        }
    }
}