using System;
using System.Net;
using Newtonsoft.Json;

namespace CoreRemoting.Serialization.Bson.Converters
{
    /// <summary>
    /// Converter to serialize and deserialize IPAddress instances
    /// </summary>
    public class IPAddressConverter : JsonConverter<IPAddress>
    {
        /// <summary>
        /// Reads an IPAddress instance from JSON.
        /// </summary>
        /// <param name="reader">JSON reader</param>
        /// <param name="objectType">Object type to be read</param>
        /// <param name="existingValue">Existing value</param>
        /// <param name="hasExistingValue">Has existing value</param>
        /// <param name="serializer">JSON serializer</param>
        /// <returns>IPAddress instance</returns>
        public override IPAddress ReadJson(JsonReader reader, Type objectType, IPAddress existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
                return null;

            var addressString = reader.Value?.ToString();
            if (string.IsNullOrEmpty(addressString))
                return null;

            return IPAddress.Parse(addressString);
        }

        /// <summary>
        /// Writes JSON for an IPAddress instance.
        /// </summary>
        /// <param name="writer">JSON writer</param>
        /// <param name="value">IPAddress</param>
        /// <param name="serializer">JSON serializer</param>
        public override void WriteJson(JsonWriter writer, IPAddress value, JsonSerializer serializer)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            writer.WriteValue(value.ToString());
        }
    }
}
