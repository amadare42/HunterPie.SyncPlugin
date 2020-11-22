using System;
using System.Linq;
using System.Threading;
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
            Logger.Target = new TestOutputLogger(testOutput);
            Logger.Prefix = "";
            Logger.LogLevel = LogLevel.Trace;
            ConfigService.Current = new Config
            {
                ServerUrl = "http://localhost:5001/dev"
            };
        }

        /// <summary>
        /// Can be visualized using https://dreampuf.github.io/
        /// </summary>
        [Fact]
        public void PrintPollServiceGraph()
        {
            var poll = new PollService();
            var graph = poll.GetStateMachineGraph();
            this.testOutput.WriteLine(graph);
        }

        [Fact]
        public void PushService_Infinite()
        {
            var push = new PushService {SessionId = SessionId};
            push.SetEnabled(true);

            while (true)
            {
                push.PushMonster(MockGenerator.GenerateModel());
                Thread.Sleep(3000);
            }
        }

        [Fact]
        public void PollService_Infinite()
        {
            var poll = new PollService {SessionId = SessionId};
            poll.SetEnabled(true);

            while (true)
            {
                using (var borrow = poll.BorrowMonsters())
                {
                    if (borrow.Value.Count != 0)
                    {
                        testOutput.WriteLine(borrow.Value.First().Id);
                        borrow.Value.Clear();
                    }
                }
                Thread.Sleep(1000);
            }
        }
    }
}