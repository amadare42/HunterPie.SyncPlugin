using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Plugin.Sync.Model;
using Plugin.Sync.Server;
using Plugin.Sync.Util;

namespace StressTest
{
    internal class Program
    {
        private static long TotalPollDelay = 0;
        private static int PollIterations = 0;
        
        public static void Main(string[] args)
        {
            Logger.Target = new ConsoleTarget();
            Logger.Prefix = "";
            Logger.LogLevel = LogLevel.Trace;
            SyncServerClient.BaseUrl = "https://amadare-mhw-sync.herokuapp.com";
            
            var server = new PushService();

            var sessionId = Guid.NewGuid().ToString();
            server.SessionId = sessionId;

            for (var i = 0; i < 3; i++)
            {
                var client = new PollService {SessionId = sessionId};

                client.SetState(true);
                PollLoop(client, i);
            }

            server.SetState(true);
            while (true)
            {
                server.PushMonster(GenerateModel());
                Thread.Sleep(150);
                Logger.Log($"Avg poll delay: {(int)(TotalPollDelay / PollIterations)} ms");
            }
        }

        private static MonsterModel GenerateModel()
        {
            return new MonsterModel
            {
                Parts = Enumerable.Repeat(0, 60).Select(_ => GeneratePart()).ToList()
            };
        }

        private static MonsterPartModel GeneratePart()
        {
            return new MonsterPartModel
            {
                Health = (float) (new Random()).NextDouble(),
                MaxHealth = (float) (new Random()).NextDouble()
            };
        }

        private static async void PollLoop(PollService client, int idx)
        {
            var sw = new Stopwatch();
            List<MonsterModel> models = null;
            while (true)
            {
                using (var borrow = client.BorrowMonsters())
                {
                    if (!borrow.Value.AreEqual(models))
                    {
                        TotalPollDelay += sw.ElapsedMilliseconds;
                        PollIterations++;
                        sw.Restart();
                        models = borrow.Value.Select(m => m.Clone()).ToList();
                        Logger.Log($"poll[{idx}]: monsters changed");
                    }
                }

                await Task.Delay(150);
            }
        } 
    }
}