using System;
using System.Linq.Expressions;
using System.Threading.Tasks;
using HunterPie.Core;
using Expression = System.Linq.Expressions.Expression;

namespace Plugin.Sync.Util
{
    public static class ReflectionsHelper
    {
        public static Action WrapActionWithName(Action action, string name)
        {
            return (Action)Expression.Lambda(
                Expression.Call(
                    Expression.Constant(action.Target),
                    action.Method
                ),
                name,
                new ParameterExpression[0]
            ).Compile();
        }
        
        public static Func<Task> WrapActionWithName(Func<Task> action, string name)
        {
            return Expression.Lambda<Func<Task>>(
                Expression.Call(
                    Expression.Constant(action.Target),
                    action.Method
                ),
                name,
                new ParameterExpression[0]
            ).Compile();
        }

        public static Action<Monster, int> CreateUpdateMonsterHealth()
        {
            var healthProp = typeof(Monster).GetProperty(nameof(Monster.Health));
            var percProp = typeof(Monster).GetProperty(nameof(Monster.HPPercentage));

            var setHealth = (Action<Monster, float>)healthProp!.GetSetMethod(true).CreateDelegate(typeof(Action<Monster, float>));
            var setPerc = (Action<Monster, float>)percProp!.GetSetMethod(true).CreateDelegate(typeof(Action<Monster, float>));

            return (monster, health) =>
            {
                setHealth(monster, health);
                setPerc(monster, monster.Health / monster.MaxHealth == 0 ? 1 : monster.Health / monster.MaxHealth);
            };
        }
    }
}