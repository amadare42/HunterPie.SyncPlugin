using System.IO;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Plugin.Sync.Connectivity;
using Plugin.Sync.Services;
using Plugin.Sync.Util;
using Xunit;
using Xunit.Abstractions;

namespace Plugin.Sync.Tests.Integration
{
    public class PushTests : BaseTests
    {
        public PushTests(ITestOutputHelper testOutput) : base(testOutput)
        {
        }

        [Fact]
        public void SyncService_Push_Infinite()
        {
            var sync = new SyncService(new DomainWebsocketClient(ConfigService.GetWsUrl())) {SessionId = "c3@uTKeQR3Mp"};
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
            var poll = new SyncService(new DomainWebsocketClient(ConfigService.GetWsUrl())) {SessionId = "c3@uTKeQR3Mp"};
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
}