using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Plugin.Sync.Connectivity;
using Plugin.Sync.Model;
using Xunit;
using Xunit.Abstractions;

namespace Plugin.Sync.Tests
{
    public class SerializationTests
    {
        private readonly ITestOutputHelper testOutput;

        public SerializationTests(ITestOutputHelper testOutput)
        {
            this.testOutput = testOutput;
        }

        [Fact]
        public void AilmentsShouldSerialize()
        {
            var serializer = BaseMessageHandler.CreateSerializer();
            
            var initialValue = new AilmentModel
            {
                Buildup = 41.23f,
                Index = 42
            };
            var serialized = SerializeToString(serializer, initialValue);
            this.testOutput.WriteLine(serialized);
            var deserialized = DeserializeFromString<AilmentModel>(serializer, serialized);
            
            Assert.Equal(initialValue, deserialized);
        }
        
        [Fact]
        public void PartShouldSerialize()
        {
            var serializer = BaseMessageHandler.CreateSerializer();
            
            var initialValue = new MonsterPartModel
            {
                Health = 32.42f,
                Index = 42
            };
            var serialized = SerializeToString(serializer, initialValue);
            this.testOutput.WriteLine(serialized);
            var deserialized = DeserializeFromString<MonsterPartModel>(serializer, serialized);
            
            Assert.Equal(initialValue, deserialized);
        }
        
        [Fact]
        public void ShouldSerializeArrays()
        {
            var serializer = BaseMessageHandler.CreateSerializer();

            var arr = new[]
            {
                new MonsterPartModel
                {
                    Health = 32.42f,
                    Index = 0
                },
                new MonsterPartModel
                {
                    Health = 23.42f,
                    Index = 1
                }
            };
            
            var serialized = SerializeToString(serializer, arr);
            this.testOutput.WriteLine(serialized);
            var deserialized = DeserializeFromString<MonsterPartModel[]>(serializer, serialized);
            
            Assert.Equal(arr, deserialized);
        }
        
        [Fact]
        public void ShouldSerializeMonster()
        {
            var serializer = BaseMessageHandler.CreateSerializer();

            var initialValue = new MonsterModel
            {
                Id = "em_001",
                Ailments = new List<AilmentModel>
                {
                    new AilmentModel
                    {
                        Buildup = 41.23f,
                        Index = 42
                    }
                },
                Parts = new List<MonsterPartModel>
                {
                    new MonsterPartModel
                    {
                        Health = 32.42f,
                        Index = 0
                    },
                    new MonsterPartModel
                    {
                        Health = 23.42f,
                        Index = 1
                    }
                }
            };
            
            var serialized = SerializeToString(serializer, initialValue);
            this.testOutput.WriteLine(serialized);
            var deserialized = DeserializeFromString<MonsterModel>(serializer, serialized);
            
            Assert.Equal(initialValue, deserialized);
        }

        private static string SerializeToString(JsonSerializer serializer, object value)
        {
            using var sw = new StringWriter();
            serializer.Serialize(sw, value);
            return sw.ToString();
        }

        private static T DeserializeFromString<T>(JsonSerializer serializer, string value)
        {
            using var reader = new StringReader(value);
            using var jsonReader = new JsonTextReader(reader);
            return serializer.Deserialize<T>(jsonReader);
        }
    }
}