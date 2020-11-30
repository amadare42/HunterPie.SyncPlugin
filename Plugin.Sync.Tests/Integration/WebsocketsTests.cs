using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HunterPie.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Plugin.Sync.Connectivity;
using Plugin.Sync.Services;
using Plugin.Sync.Util;
using Xunit;
using Xunit.Abstractions;

namespace Plugin.Sync.Tests
{
    public class PushTests
    {
        private readonly ITestOutputHelper testOutput;
        private const string SessionId = "test";

        public PushTests(ITestOutputHelper testOutput)
        {
            this.testOutput = testOutput;
            Logger.Targets = new List<ILoggerTarget>
            {
                new TestOutputLogger(testOutput)
            };
            Logger.Prefix = "";
            Logger.LogLevel = LogLevel.Trace;
            ConfigService.Current = new Config
            {
                ServerUrl = "http://localhost:5001/dev"
            };
            UserSettings.PlayerConfig = new UserSettings.Config.Rootobject {Overlay = {GameScanDelay = 150}};
        }

        /// <summary>
        /// Can be visualized using https://dreampuf.github.io/GraphvizOnline/
        /// </summary>
        [Fact]
        public void PrintPollServiceGraph()
        {
            var poll = new SyncService();
            var graph = poll.GetStateMachineGraph();
            this.testOutput.WriteLine(graph);
        }

        [Fact]
        public void SyncService_Push_Infinite()
        {
            var sync = new SyncService {SessionId = "c3@uTKeQR3Mp"};
            sync.SetMode(SyncServiceMode.Push);

            while (true)
            {
                var generateModel = MockGenerator.GenerateModel();
                sync.PushMonster(generateModel);
                Thread.Sleep(150);
            }
        }

        [Fact]
        public void PollService_Infinite()
        {
            var poll = new SyncService {SessionId = "c3@uTKeQR3Mp"};
            poll.SetMode(SyncServiceMode.Poll);

            while (true)
            {
                using (var borrow = poll.BorrowMonsters())
                {
                    if (borrow.Value.Count != 0)
                    {
                        var text = JsonConvert.SerializeObject(borrow.Value, Formatting.Indented);
                        Logger.Info(text);
                        borrow.Value.Clear();
                    }
                }
                Thread.Sleep(1000);
            }
        }
    }

    public class PrintMessageHandler : BaseMessageHandler, IMessageHandler
    {
        public void ReceiveMessage(Stream stream)
        {
            var sr = new StreamReader(stream, Encoding.UTF8);
            using var jsonReader = new JsonTextReader(sr);
            var jObj = JObject.Load(jsonReader);
            Logger.Info($"[rcvd] ${jObj.ToString(Formatting.Indented)}");
        }
    }
}