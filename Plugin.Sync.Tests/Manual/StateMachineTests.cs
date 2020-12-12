using Plugin.Sync.Connectivity;
using Plugin.Sync.Sync;
using Stateless;
using Xunit;
using Xunit.Abstractions;

namespace Plugin.Sync.Tests.Manual
{
    public class StateMachineTests : BaseTests
    {
        public StateMachineTests(ITestOutputHelper testOutput) : base(testOutput)
        {
        }
        
        [Fact]
        public void ReenterWillCallExitAndEntry()
        {
            var stateMachine = new StateMachine<string, string>("init");
            var result = "";
            stateMachine.Configure("init")
                .Permit("toFoo", "foo");

            stateMachine.Configure("foo")
                .OnEntry(() => result += "e")
                .OnExit(() => result += "x")
                .PermitReentry("toFoo");
            
            stateMachine.Fire("toFoo");
            stateMachine.Fire("toFoo");
            stateMachine.Fire("toFoo");
            
            Assert.Equal("exexe", result);
        }

        #if DEBUG
        /// <summary>
        /// Can be visualized using https://dreampuf.github.io/GraphvizOnline/
        /// </summary>
        [Fact]
        public void PrintPollServiceGraph()
        {
            var poll = new SyncService(new DomainWebsocketClient(ConfigService.GetWsUrl()));
            var graph = poll.GetStateMachineGraph();
            this.TestOutput.WriteLine(graph);
        }
        #endif
    }
}