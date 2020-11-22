using System;
using Newtonsoft.Json;

namespace Plugin.Sync.Util
{
    public class LimitFloatPrecisionConverter : JsonConverter
    {
        private readonly int precision;

        public LimitFloatPrecisionConverter(int precision)
        {
            this.precision = precision;
        }

        public override bool CanConvert(Type objectType)
        {
            return (objectType == typeof(float));
        }

        public override void WriteJson(JsonWriter writer, object value, 
            JsonSerializer serializer)
        {
            if ((float) value == 0)
            {
                writer.WriteValue(0);
            }
            else
            {
                writer.WriteValue(Math.Round((float)value, this.precision));
            }
        }

        public override bool CanRead => false;

        public override object ReadJson(JsonReader reader, Type objectType,
            object existingValue, JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }
    }
}