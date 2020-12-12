using System;
using System.Collections.Generic;
using System.Linq;
using Stateless;
using Stateless.Reflection;

namespace Plugin.Sync.Util
{
    public class TransitionInfo<TState>
    {
        public string Name { get; }

        public TransitionInfo(string name)
        {
            this.Name = name;
        }

        public Func<TState> Fn { get; set; }

        public Dictionary<TState, string> Destinations  { get; set; } = new Dictionary<TState, string>();
    }

    public static class StateMachineExtensions
    {
        public static StateMachine<TState, TTrigger>.StateConfiguration Permit<TState, TTrigger>(
            this StateMachine<TState, TTrigger>.StateConfiguration stateMachine,
            TTrigger trigger,
            TransitionInfo<TState> transitionInfo
        )
        {
            var dynStateInfos = new DynamicStateInfos();
            if (transitionInfo.Destinations != null)
            {
                dynStateInfos.AddRange(
                    transitionInfo.Destinations.Select(d => new DynamicStateInfo(d.Key.ToString(), d.Value)));
            }

            return stateMachine.PermitDynamic(trigger, transitionInfo.Fn, transitionInfo.Name, dynStateInfos);
        }
    }
}