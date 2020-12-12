using System;
using System.Linq;
using Plugin.Sync.Model;

namespace Plugin.Sync.Tests.Manual
{
    public static class MockGenerator
    {
        private static readonly Random Random = new Random();
        
        public static MonsterModel GenerateModel()
        {
            return new MonsterModel
            {
                Id = "em_001",
                Parts = Enumerable.Range(0, 30).Select(GeneratePartClose).ToList()
            };
        }
        
        private static MonsterPartModel GeneratePartClose(int idx)
        {
            return new MonsterPartModel
            {
                Index = idx,
                Health = Random.Next(97, 100)
            };
        }
    }
}