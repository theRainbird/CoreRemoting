using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Threading;
using Newtonsoft.Json;

namespace CoreRemoting.Serialization.Bson.Converters
{
    /// <summary>
    /// High-performance converter to serialize and deserialize Hashtable instances while preserving original data types.
    /// Prevents int values from being deserialized as long values by storing type information.
    /// </summary>
    public class HashtableConverter : JsonConverter<Hashtable>
    {
        // Type caches for performance optimization
        private static readonly ConcurrentDictionary<Type, string> _typeToNameCache = new();
        private static readonly ConcurrentDictionary<string, Type> _nameToTypeCache = new();
        private static readonly ConcurrentDictionary<Type, PrimitiveType> _typeToPrimitiveCache = new();

        // Pre-allocated string builders for serialization
        private static readonly ThreadLocal<System.Text.StringBuilder> _stringBuilder = 
            new(() => new System.Text.StringBuilder(256));

        /// <summary>
        /// Determines whether this instance can read JSON.
        /// </summary>
        /// <returns>true if this instance can read JSON; otherwise, false</returns>
        public override bool CanRead => true;

        /// <summary>
        /// Determines whether this instance can write JSON.
        /// </summary>
        /// <returns>true if this instance can write JSON; otherwise, false</returns>
        public override bool CanWrite => true;

        /// <summary>
        /// Reads a Hashtable instance from JSON.
        /// </summary>
        /// <param name="reader">JSON reader</param>
        /// <param name="objectType">Object type to be read</param>
        /// <param name="existingValue">Existing value</param>
        /// <param name="hasExistingValue">Has existing value</param>
        /// <param name="serializer">JSON serializer</param>
        /// <returns>Hashtable instance</returns>
        public override Hashtable ReadJson(JsonReader reader, Type objectType, Hashtable existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
                return null;

            if (reader.TokenType != JsonToken.StartObject)
                throw new JsonSerializationException($"Expected StartObject token, got {reader.TokenType}");

            var hashtable = existingValue ?? new Hashtable();

            while (reader.Read())
            {
                if (reader.TokenType == JsonToken.EndObject)
                    break;

                if (reader.TokenType != JsonToken.PropertyName)
                    throw new JsonSerializationException($"Expected PropertyName token, got {reader.TokenType}");

                var propertyName = reader.Value?.ToString();

                // Read value token
                if (!reader.Read())
                    throw new JsonSerializationException("Unexpected end when reading Hashtable.");

                // Skip optional $type metadata that may be present to allow object-typed fields to deserialize as Hashtable
                if (string.Equals(propertyName, "$type", StringComparison.Ordinal))
                {
                    // value is a string type name – ignore and continue to next property
                    continue;
                }

                // For all other properties we expect an entry object
                if (reader.TokenType != JsonToken.StartObject)
                    throw new JsonSerializationException($"Expected StartObject for entry, got {reader.TokenType}");

                var (key, value) = ReadEntryOptimized(reader, serializer);
                hashtable[key] = value;
            }

            return hashtable;
        }

        /// <summary>
        /// Writes JSON for a Hashtable instance.
        /// </summary>
        /// <param name="writer">JSON writer</param>
        /// <param name="value">Hashtable</param>
        /// <param name="serializer">JSON serializer</param>
        public override void WriteJson(JsonWriter writer, Hashtable value, JsonSerializer serializer)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            writer.WriteStartObject();

            // Write $type if this Hashtable is likely to be serialized in an object-typed slot with high probability. Two triggers:
            //  1) An explicit context flag (if available) – generic and independent of the property name.
            //  2) Heuristic for lists/arrays of POCOs: paths like "[0].Foo" contain "].".
            var path = writer.Path;
            if (ObjectTypeSlotContext.InObjectSlot || (!string.IsNullOrEmpty(path) && path.Contains("].") && !path.EndsWith(".V", StringComparison.Ordinal)))
            {
                var hashtableTypeName = GetTypeNameCached(value?.GetType() ?? typeof(Hashtable));
                writer.WritePropertyName("$type");
                writer.WriteValue(hashtableTypeName);
            }

            foreach (DictionaryEntry entry in value)
            {
                var entryKey = entry.Key?.ToString() ?? string.Empty;
                writer.WritePropertyName(entryKey);

                WriteEntryOptimized(writer, entry.Key, entry.Value, serializer);
            }

            writer.WriteEndObject();
        }

        /// <summary>
        /// Reads an entry with optimized manual parsing.
        /// </summary>
        private (object key, object value) ReadEntryOptimized(JsonReader reader, JsonSerializer serializer)
        {
            object key = null;
            object value = null;
            string keyTypeName = null;
            string valueTypeName = null;
            PrimitiveType? keyPrimitiveType = null;
            PrimitiveType? valuePrimitiveType = null;

            while (reader.Read())
            {
                if (reader.TokenType == JsonToken.EndObject)
                    break;

                if (reader.TokenType != JsonToken.PropertyName)
                    continue;

                var propertyName = reader.Value?.ToString();
                reader.Read(); // Move to value

                switch (propertyName)
                {
                    case "KT":
                        keyTypeName = reader.Value?.ToString();
                        break;
                    case "K":
                        key = reader.Value;
                        break;
                    case "VT":
                        valueTypeName = reader.Value?.ToString();
                        break;
                    case "V":
                        // For complex types, we need to deserialize the full object
                        if (valueTypeName != null && GetPrimitiveType(GetTypeFromNameCached(valueTypeName)) == PrimitiveType.Complex)
                        {
                            var targetType = GetTypeFromNameCached(valueTypeName);
                            if (targetType != null)
                            {
                                value = serializer.Deserialize(reader, targetType);
                            }
                            else
                            {
                                value = reader.Value;
                            }
                        }
                        else
                        {
                            value = reader.Value;
                        }
                        break;
                    case "KP":
                        keyPrimitiveType = (PrimitiveType)Convert.ToByte(reader.Value);
                        break;
                    case "VP":
                        valuePrimitiveType = (PrimitiveType)Convert.ToByte(reader.Value);
                        break;
                }
            }

            // Convert types if needed
            if (key != null)
            {
                if (keyTypeName != null)
                    key = ConvertValueOptimized(key, keyTypeName);
                else if (keyPrimitiveType.HasValue)
                    key = ConvertValueFromPrimitiveType(key, keyPrimitiveType.Value);
            }
            
            if (value != null)
            {
                if (valueTypeName != null)
                    value = ConvertValueOptimized(value, valueTypeName);
                else if (valuePrimitiveType.HasValue)
                    value = ConvertValueFromPrimitiveType(value, valuePrimitiveType.Value);
            }

            return (key, value);
        }

        /// <summary>
        /// Writes a Hashtable entry as JSON.
        /// </summary>
        private void WriteEntryOptimized(JsonWriter writer, object key, object value, JsonSerializer serializer)
        {
            writer.WriteStartObject();

            // Write key type and value
            var keyType = key?.GetType();
            var valueType = value?.GetType();

            if (keyType != null)
            {
                var keyPrimitiveType = GetPrimitiveType(keyType);
                if (keyPrimitiveType != PrimitiveType.Complex && keyPrimitiveType != PrimitiveType.Enum)
                {
                    writer.WritePropertyName("KP");
                    writer.WriteValue((byte)keyPrimitiveType);
                }
                else
                {
                    writer.WritePropertyName("KT");
                    writer.WriteValue(GetTypeNameCached(keyType));
                }
            }

            writer.WritePropertyName("K");
            writer.WriteValue(key);

            // Write value type and value
            if (valueType != null)
            {
                var valuePrimitiveType = GetPrimitiveType(valueType);
                if (valuePrimitiveType != PrimitiveType.Complex && valuePrimitiveType != PrimitiveType.Enum)
                {
                    writer.WritePropertyName("VP");
                    writer.WriteValue((byte)valuePrimitiveType);
                }
                else
                {
                    writer.WritePropertyName("VT");
                    writer.WriteValue(GetTypeNameCached(valueType));
                }
            }

            writer.WritePropertyName("V");
            
            // For complex types, use the serializer to properly serialize them
            if (valueType != null && GetPrimitiveType(valueType) == PrimitiveType.Complex)
            {
                serializer.Serialize(writer, value);
            }
            else
            {
                writer.WriteValue(value);
            }

            writer.WriteEndObject();
        }

        /// <summary>
        /// Converts a value from a primitive type enum.
        /// </summary>
        private object ConvertValueFromPrimitiveType(object value, PrimitiveType primitiveType)
        {
            if (value == null)
                return null;

            switch (primitiveType)
            {
                case PrimitiveType.Int32:
                    return Convert.ToInt32(value);
                case PrimitiveType.Int64:
                    return Convert.ToInt64(value);
                case PrimitiveType.String:
                    return value.ToString();
                case PrimitiveType.Boolean:
                    return Convert.ToBoolean(value);
                case PrimitiveType.Double:
                    return Convert.ToDouble(value);
                case PrimitiveType.Float:
                    return Convert.ToSingle(value);
                case PrimitiveType.Decimal:
                    return Convert.ToDecimal(value);
                case PrimitiveType.DateTime:
                    return Convert.ToDateTime(value);
                case PrimitiveType.Char:
                    return Convert.ToChar(value);
                case PrimitiveType.Byte:
                    return Convert.ToByte(value);
                case PrimitiveType.SByte:
                    return Convert.ToSByte(value);
                case PrimitiveType.Int16:
                    return Convert.ToInt16(value);
                case PrimitiveType.UInt16:
                    return Convert.ToUInt16(value);
                case PrimitiveType.UInt32:
                    return Convert.ToUInt32(value);
                case PrimitiveType.UInt64:
                    return Convert.ToUInt64(value);
                case PrimitiveType.Enum:
                    // For enums, we need the type information from VT field
                    // This method is called when we have primitive type info only
                    // So we can't handle enum conversion here without type info
                    return value;
                default:
                    return value;
            }
        }

        /// <summary>
        /// Converts a value to the specified type with optimized type resolution.
        /// </summary>
        private object ConvertValueOptimized(object value, string typeName)
        {
            if (value == null || typeName == null)
                return null;

            var targetType = GetTypeFromNameCached(typeName);
            
            if (targetType == null)
                return value;

            // Fast path for primitive types
            var primitiveType = GetPrimitiveType(targetType);
            
            switch (primitiveType)
            {
                case PrimitiveType.Int32:
                    return Convert.ToInt32(value);
                case PrimitiveType.Int64:
                    return Convert.ToInt64(value);
                case PrimitiveType.String:
                    return value.ToString();
                case PrimitiveType.Boolean:
                    return Convert.ToBoolean(value);
                case PrimitiveType.Double:
                    return Convert.ToDouble(value);
                case PrimitiveType.Float:
                    return Convert.ToSingle(value);
                case PrimitiveType.Decimal:
                    return Convert.ToDecimal(value);
                case PrimitiveType.DateTime:
                    return Convert.ToDateTime(value);
                case PrimitiveType.Char:
                    return Convert.ToChar(value);
                case PrimitiveType.Byte:
                    return Convert.ToByte(value);
                case PrimitiveType.SByte:
                    return Convert.ToSByte(value);
                case PrimitiveType.Int16:
                    return Convert.ToInt16(value);
                case PrimitiveType.UInt16:
                    return Convert.ToUInt16(value);
                case PrimitiveType.UInt32:
                    return Convert.ToUInt32(value);
                case PrimitiveType.UInt64:
                    return Convert.ToUInt64(value);
                case PrimitiveType.Enum:
                    return Enum.ToObject(targetType, value);
                default:
                    // For complex types, return as-is since they should already be deserialized correctly
                    return value;
            }
        }

        /// <summary>
        /// Gets type name with caching for performance.
        /// </summary>
        private string GetTypeNameCached(Type type)
        {
            if (type == null)
                return null;

            return _typeToNameCache.GetOrAdd(type, t => 
            {
                var sb = _stringBuilder.Value;
                sb.Clear();
                sb.Append(t.FullName);
                sb.Append(", ");
                sb.Append(t.Assembly.GetName().Name);
                return sb.ToString();
            });
        }

        /// <summary>
        /// Gets type from name with caching for performance.
        /// </summary>
        private Type GetTypeFromNameCached(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
                return null;

            return _nameToTypeCache.GetOrAdd(typeName, Type.GetType);
        }

        /// <summary>
        /// Gets primitive type enum for a given Type with caching.
        /// </summary>
        private PrimitiveType GetPrimitiveType(Type type)
        {
            if (type == null)
                return PrimitiveType.Unknown;

            return _typeToPrimitiveCache.GetOrAdd(type, t =>
            {
                if (t == typeof(int)) return PrimitiveType.Int32;
                if (t == typeof(long)) return PrimitiveType.Int64;
                if (t == typeof(string)) return PrimitiveType.String;
                if (t == typeof(bool)) return PrimitiveType.Boolean;
                if (t == typeof(double)) return PrimitiveType.Double;
                if (t == typeof(float)) return PrimitiveType.Float;
                if (t == typeof(decimal)) return PrimitiveType.Decimal;
                if (t == typeof(DateTime)) return PrimitiveType.DateTime;
                if (t == typeof(char)) return PrimitiveType.Char;
                if (t == typeof(byte)) return PrimitiveType.Byte;
                if (t == typeof(sbyte)) return PrimitiveType.SByte;
                if (t == typeof(short)) return PrimitiveType.Int16;
                if (t == typeof(ushort)) return PrimitiveType.UInt16;
                if (t == typeof(uint)) return PrimitiveType.UInt32;
                if (t == typeof(ulong)) return PrimitiveType.UInt64;
                if (t.IsEnum) return PrimitiveType.Enum;
                return PrimitiveType.Complex;
            });
        }
    }
}