using System;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Plugin.Sync.Util
{
    [AttributeUsage(AttributeTargets.Class)]
    public class JsonArrayObjectAttribute : Attribute
    {
    }
    
    [AttributeUsage(AttributeTargets.Property)]
    public class JsonArrayPropAttribute : Attribute
    {
        public int Index { get; set; }
    }

    /// <summary>
    /// Adds support to serialize/deserialize objects to arrays. ( e.g. { foo: 42, bar: 'hello' } to [42,'hello'].
    /// Only properties marked with JsonArrayProp attribute will be included.
    /// </summary>
    public class JsonArrayObjectConverter : JsonConverter
    {
        public override void WriteJson(JsonWriter writer, object obj, JsonSerializer serializer)
        {
            var arr = obj.GetType().GetProperties()
                .Select(p => new {p, index = p.GetCustomAttribute<JsonArrayPropAttribute>()?.Index})
                .Where(p => p.index != null)
                .OrderBy(p => p.index)
                .Select(p => p.p.GetValue(obj))
                .ToArray();
            
            serializer.Serialize(writer, arr);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            var obj = Activator.CreateInstance(objectType);
            var props = objectType.GetProperties()
                .Select(p => new {p, index = p.GetCustomAttribute<JsonArrayPropAttribute>()?.Index})
                .Where(p => p.index != null)
                .OrderBy(p => p.index)
                .ToList();
            var arr = ReadArrayObject(reader, serializer, props.Count);
            for (var i = 0; i < arr.Count; i++)
            {
                var jtoken = arr[i];
                var prop = props[i].p;

                var value = serializer.Deserialize(jtoken.CreateReader(), prop.PropertyType);
                prop.SetValue(obj, value);
            }

            return obj;
        }
        
        private static JArray ReadArrayObject(JsonReader reader, JsonSerializer serializer, int expectedLength)
        {
            if (!(serializer.Deserialize<JToken>(reader) is JArray arr) || arr.Count != expectedLength)
                throw new JsonSerializationException($"Expected array of length {expectedLength}"); 
            return arr;
        }

        public override bool CanConvert(Type objectType)
        {
            return objectType.GetCustomAttribute(typeof(JsonArrayObjectAttribute)) != null;
        }
    }
}