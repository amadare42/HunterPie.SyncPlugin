using System;
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace Plugin.Sync.Connectivity
{
    /// <summary>
    /// Encodes data as json. Reuses same buffers to avoid per-message memory allocation. Not thread safe.
    /// </summary>
    public class JsonMessageEncoder : BaseMessageHandler, IMessageEncoder
    {
        private readonly StringBuilder stringBuilderBuffer = new StringBuilder(512);
        private readonly char[] charBuffer = new char[MaxMessageSize];
        
        (char[], int) IMessageEncoder.EncodeMessage(object data)
        {
            this.stringBuilderBuffer.Clear();
            
            // serialize object to string builder
            var sw = new StringWriter(this.stringBuilderBuffer, CultureInfo.InvariantCulture);
            using var jsonWriter = new JsonTextWriter(sw) {Formatting = Formatting.None};
            this.Serializer.Serialize(jsonWriter, data);
            
            // copy characters from string builder to character buffer
            if (this.charBuffer.Length < this.stringBuilderBuffer.Length)
            {
                throw new Exception($"Message is too big ({this.stringBuilderBuffer.Length}");
            }
            this.stringBuilderBuffer.CopyTo(0, this.charBuffer, 0, this.stringBuilderBuffer.Length);

            return (this.charBuffer, this.stringBuilderBuffer.Length);
        }
    }
}