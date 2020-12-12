using System.Threading.Tasks;
using Plugin.Sync.Logging;
using Xunit;
using Xunit.Abstractions;

namespace Plugin.Sync.Tests.Manual
{
    public class LoggingTests : BaseTests
    {
        public LoggingTests(ITestOutputHelper testOutput) : base(testOutput)
        {
        }

        [Fact]
        public async Task Log()
        {
            var target = new ServerLoggerTarget("test-user", "some room");

            for (var i = 0; i < 100; i++)
            {
                target.Log("logging entry #" + i, LogLevel.Info);
            }

            await target.Close();
        }
        
        [Fact]
        public async Task LogOnTimeout()
        {
            var target = new ServerLoggerTarget("test-user", "");
            target.Log("logging entry single entry", LogLevel.Info);
            Logger.Info("Logged!");
            await Task.Delay(10000);

            await target.Close();
        }
    }
}