using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using HunterPie.Core;
using Plugin.Sync.Model;
using Plugin.Sync.Util;

namespace Plugin.Sync
{
    public static class ReflectionsHelper
    {
        public static void UpdateMonsterScanRefs(Monster monster, ThreadStart scanRef, Thread scan)
        {
            typeof(Monster).GetField("monsterInfoScanRef", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(monster, scanRef);
            typeof(Monster).GetField("monsterInfoScan", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(monster, scan);
        }
        
        public static Action<Monster> CreateLeadersMonsterUpdateFn()
        {

            // there should be all methods from ScanMonsterInfo method
            string[] methodsToCall =
            {
                "GetMonsterAddress",
                "GetMonsterId",
                "GetMonsterSizeModifier",
                "GetMonsterStamina",
                "GetMonsterAilments",
                "GetMonsterStamina",
                "GetMonsterPartsInfo",
                "GetPartsTenderizeInfo",
                "GetMonsterAction",
                "GetMonsterEnrageTimer",
                "GetTargetMonsterAddress",
                "GetAlatreonCurrentElement"
            };
            return CombineActions(methodsToCall);
        }
        public static Action<Monster> CreatePeersMonsterUpdateFn()
        {
            // methods that produce values that should be pulled from server are commented
            string[] methodsToCall =
            {
                "GetMonsterAddress",
                "GetMonsterId",
                "GetMonsterSizeModifier",
                "GetMonsterStamina",
                // "GetMonsterAilments",
                "GetMonsterStamina",
                // "GetMonsterPartsInfo",
                // "GetPartsTenderizeInfo",
                "GetMonsterAction",
                "GetMonsterEnrageTimer",
                "GetTargetMonsterAddress",
                "GetAlatreonCurrentElement"
            };
            return CombineActions(methodsToCall);
        }

        private static Action<Monster> CombineActions(string[] methods)
        {
            var actions = methods
                .Select(CreateUpdateMonsterAction)
                .Where((fn, idx) =>
                {
                    if (fn == null)
                    {
                        Logger.Error(
                            $"Cannot override monster function '{methods[idx]}'. Application will behave unpredictably.");
                        return false;
                    }

                    return true;
                })
                .ToArray();

            return monster =>
            {
                foreach (var action in actions)
                {
                    action(monster);
                }
            };
        }

        public static void StopMonsterThread(Monster monster)
        {
            typeof(Monster).GetMethod("StopThread", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .Invoke(monster, new object[0]);
        }

        /// <summary>
        /// Creates delegate that call private method with provided name on Monster type.
        /// </summary>
        public static Action<Monster> CreateUpdateMonsterAction(string name) =>
            (Action<Monster>)typeof(Monster).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)?
                .CreateDelegate(typeof(Action<Monster>));
    }
}
