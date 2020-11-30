using Stateless;
using Xunit;

namespace Plugin.Sync.Tests
{
    public class StateMachineTests
    {
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
            
            Assert.Equal(result, "exexe");
        }
        
    }
}