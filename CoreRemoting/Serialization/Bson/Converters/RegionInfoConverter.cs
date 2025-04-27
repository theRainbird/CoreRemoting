using System;
using System.Globalization;
using Newtonsoft.Json;

namespace CoreRemoting.Serialization.Bson.Converters
{
    /// <summary>
    /// Converter to serialize and deserialize RegionInfo instances
    /// </summary>
    public class RegionInfoConverter : JsonConverter<RegionInfo>
    {
        /// <summary>
        /// Reads a RegionInfo instance from JSON.
        /// </summary>
        /// <param name="reader">JSON reader</param>
        /// <param name="objectType">Object type to be read</param>
        /// <param name="existingValue">Existing value</param>
        /// <param name="hasExistingValue">Has existing value</param>
        /// <param name="serializer">JSON serializer</param>
        /// <returns>RegionInfo instance</returns>
        public override RegionInfo ReadJson(JsonReader reader, Type objectType, RegionInfo existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
                return null;

            var regionName = reader.Value?.ToString();
            if (string.IsNullOrEmpty(regionName))
                return null;

            return new RegionInfo(regionName);
        }

        /// <summary>
        /// Writes JSON for a RegionInfo instance.
        /// </summary>
        /// <param name="writer">JSON writer</param>
        /// <param name="value">RegionInfo</param>
        /// <param name="serializer">JSON serializer</param>
        public override void WriteJson(JsonWriter writer, RegionInfo value, JsonSerializer serializer)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            writer.WriteValue(value.Name);
        }
    }
}
