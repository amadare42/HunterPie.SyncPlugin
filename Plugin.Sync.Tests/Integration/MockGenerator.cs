using System;
using System.Linq;
using System.Text.RegularExpressions;
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
                Parts = Enumerable.Repeat(0, 60).Select(_ => GeneratePart()).ToList()
            };
        }
        
        private static MonsterPartModel GeneratePart()
        {
            return new MonsterPartModel
            {
                Health = (float) random.NextDouble(),
                MaxHealth = (float) random.NextDouble()
            };
        }
    }
}