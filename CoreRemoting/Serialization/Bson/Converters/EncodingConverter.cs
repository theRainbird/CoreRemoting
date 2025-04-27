using System;
using System.Text;
using Newtonsoft.Json;

namespace CoreRemoting.Serialization.Bson.Converters
{
    /// <summary>
    /// Converter to serialize and deserialize Encoding instances.
    /// </summary>
    public class EncodingConverter : JsonConverter<Encoding>
    {
        /// <summary>
        /// Reads an Encoding instance from JSON.
        /// </summary>
        /// <param name="reader">JSON reader</param>
        /// <param name="objectType">Object type to be read</param>
        /// <param name="existingValue">Existing value</param>
        /// <param name="hasExistingValue">Has existing value</param>
        /// <param name="serializer">JSON serializer</param>
        /// <returns>Encoding instance</returns>
        public override Encoding ReadJson(JsonReader reader, Type objectType, Encoding existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
                return null;

            var encodingName = reader.Value?.ToString();
            if (string.IsNullOrEmpty(encodingName))
                return null;

            return Encoding.GetEncoding(encodingName);
        }

        /// <summary>
        /// Writes JSON for an Encoding instance.
        /// </summary>
        /// <param name="writer">JSON writer</param>
        /// <param name="value">Encoding</param>
        /// <param name="serializer">JSON serializer</param>
        public override void WriteJson(JsonWriter writer, Encoding value, JsonSerializer serializer)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            writer.WriteValue(value.WebName); // IANA names should be compatible with Encoding.GetEncoding
        }
    }
}
