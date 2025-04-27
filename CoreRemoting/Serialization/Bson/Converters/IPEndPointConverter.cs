using System;
using System.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CoreRemoting.Serialization.Bson.Converters
{
    /// <summary>
    /// Converter to serialize and deserialize IPEndPoint instances.
    /// </summary>
    public class IPEndPointConverter : JsonConverter<IPEndPoint>
    {
        /// <summary>
        /// Reads an IPEndPoint instance from JSON.
        /// </summary>
        /// <param name="reader">JSON reader</param>
        /// <param name="objectType">Object type to be read</param>
        /// <param name="existingValue">Existing value</param>
        /// <param name="hasExistingValue">Indicates if the existing value exists</param>
        /// <param name="serializer">JSON serializer</param>
        /// <returns>IPEndPoint instance</returns>
        public override IPEndPoint ReadJson(JsonReader reader, Type objectType, IPEndPoint existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            // Read the JSON value as a JObject to extract both Address and Port
            var jsonObject = JObject.Load(reader);
            var address = jsonObject["Address"]?.ToObject<IPAddress>(serializer);
            var port = jsonObject["Port"]?.ToObject<int>(serializer);

            if (address == null || port == null)
                throw new JsonSerializationException("Invalid IPEndPoint format.");

            return new IPEndPoint(address, port.Value);
        }

        /// <summary>
        /// Writes JSON for an IPEndPoint instance.
        /// </summary>
        /// <param name="writer">JSON writer</param>
        /// <param name="value">IPEndPoint</param>
        /// <param name="serializer">JSON serializer</param>
        public override void WriteJson(JsonWriter writer, IPEndPoint value, JsonSerializer serializer)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            // Serialize IPEndPoint as a JSON object with "Address" and "Port" properties
            writer.WriteStartObject();
            writer.WritePropertyName("Address");
            serializer.Serialize(writer, value.Address);
            writer.WritePropertyName("Port");
            serializer.Serialize(writer, value.Port);
            writer.WriteEndObject();
        }
    }
}