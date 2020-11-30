using System;
using System.Linq;
using Plugin.Sync.Model;

namespace Plugin.Sync.Tests
{
    public static class MockGenerator
    {
        private static Random random = new Random();
        
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
                Health = random.Next(97, 100),
                MaxHealth = 100
            };
        }
        
        // private static MonsterPartModel GeneratePart(int idx)
        // {
        //     return new MonsterPartModel
        //     {
        //         Index = idx,
        //         Health = (float) random.NextDouble() + random.Next(100, 2000),
        //         MaxHealth = (float) random.NextDouble() + random.Next(100, 2000)
        //     };
        // }
    }
}