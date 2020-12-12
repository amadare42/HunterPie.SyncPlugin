using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Newtonsoft.Json.Linq;

namespace Plugin.Sync.Logging
{
    public class ServerLoggerTarget : ILoggerTarget
    {
        private readonly string user;
        private readonly string room;
        private readonly HttpClient client = new HttpClient();

        private readonly ITargetBlock<JObject> logs;
        private Task completionTask;

        public ServerLoggerTarget(string user, string room)
        {
            this.user = user;
            this.room = room;
            this.logs = CreatePipeline();
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

        private ITargetBlock<JObject> CreatePipeline()
        {
            var logBuffer = new BufferBlock<JObject>();
            
            // dispatching logs in array of 6 or after 5 sec timeout
            var batchStep = new BatchBlock<JObject>(5);
            var timeoutBatchTimer = new Timer(_ => batchStep.TriggerBatch());
            var timerStep = new TransformBlock<JObject, JObject>(val =>
            {
                timeoutBatchTimer.Change(5000, Timeout.Infinite);
                return val;
            });
            
            // combining logs entries to array
            var combineArrayStep = new TransformBlock<JObject[], JArray>(ObjectsToJArray);
            
            // ship logs
            var shipStep = new ActionBlock<JArray>(ShipLogs, new ExecutionDataflowBlockOptions
            {
                MaxDegreeOfParallelism = 3
            });

            timerStep.LinkTo(batchStep, new DataflowLinkOptions { PropagateCompletion = true });
            logBuffer.LinkTo(timerStep, new DataflowLinkOptions{ PropagateCompletion = true });
            batchStep.LinkTo(combineArrayStep, new DataflowLinkOptions{ PropagateCompletion = true });
            combineArrayStep.LinkTo(shipStep, new DataflowLinkOptions{ PropagateCompletion = true });

            this.completionTask = shipStep.Completion;

            return logBuffer;
        }

        private static JArray ObjectsToJArray(JObject[] entries)
        {
            var arr = new JArray();
            
            foreach (var jObject in entries)
            {
                arr.Add(jObject);
            }

            return arr;
        }

        private async Task ShipLogs(JArray entry)
        {
            var content = new StringContent(entry.ToString(), Encoding.UTF8, "application/json");
            try
            {
                await this.client.PostAsync(ConfigService.Current.ServerUrl.TrimEnd('/') + "/logs/add", content);
            }
            catch (Exception)
            {
                // ignored
            }
        }

        public async Task Close()
        {
            this.logs.Complete();
            await this.completionTask;
        }

        ~ServerLoggerTarget()
        {
            // give it 5 seconds to finish shipping
            Close().Wait(TimeSpan.FromSeconds(5));
        }
    }
}