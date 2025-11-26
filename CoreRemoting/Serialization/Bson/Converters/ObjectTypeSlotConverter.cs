using System;
using Newtonsoft.Json;

namespace CoreRemoting.Serialization.Bson.Converters
{
    /// <summary>
    /// Provides a lightweight context to detect when a value is being written into a slot
    /// that is declared as type 'object'. This allows other converters (e.g., HashtableConverter)
    /// to adjust emitted metadata without relying on brittle path heuristics.
    /// </summary>
    internal static class ObjectTypeSlotContext
    {
        [ThreadStatic] private static int _depth;

        public static bool InObjectSlot => _depth > 0;

        public static void Enter() => _depth++;
        public static void Exit()
        {
            if (_depth > 0) _depth--;
        }
    }

    /// <summary>
    /// Json.NET converter that is only selected for members declared as 'object'.
    /// It sets a thread-local context flag while delegating the actual serialization
    /// to the configured serializer, so other converters can react accordingly.
    /// </summary>
    internal class ObjectTypeSlotConverter : JsonConverter
    {
        /// <inheritdoc/>
        public override bool CanRead => false;
        
        /// <inheritdoc/>
        public override bool CanWrite => true;

        /// <inheritdoc/>
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(object);
        }

        /// <inheritdoc/>
        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            return serializer.Deserialize(reader);
        }

        /// <inheritdoc/>
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            ObjectTypeSlotContext.Enter();
            try
            {
                if (value != null && value.GetType() == typeof(object))
                {
                    writer.WriteStartObject();
                    writer.WriteEndObject();
                    return;
                }

                serializer.Serialize(writer, value);
            }
            finally
            {
                ObjectTypeSlotContext.Exit();
            }
        }
    }
}
