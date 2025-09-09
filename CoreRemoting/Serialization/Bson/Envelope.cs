using System;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using CoreRemoting.Serialization.Bson.Converters.DataSetDiffGramSupport;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CoreRemoting.Serialization.Bson
{
    /// <summary>
    /// Wraps values and preserve their types for serialization.
    /// </summary>
    [SuppressMessage("ReSharper", "ConvertToAutoProperty")]
    public class Envelope
    {
        [JsonProperty]
        private object _value;
        
        [JsonProperty]
        [JsonConverter(typeof(TypeIgnoreVersionConverter))]
        private Type _type;
        
        /// <summary>
        /// Creates a new instance of the Envelope class.
        /// </summary>
        public Envelope() { }

        /// <summary>
        /// Creates a new instance of the Envelope class.
        /// </summary>
        /// <param name="value">Value to wrap</param>
        public Envelope(object value)
        {
            _value = value;

            if (_value != null)
                _type = _value.GetType();
        }

        /// <summary>
        /// Gets the type of the wrapped value.
        /// </summary>
        [JsonIgnore]
        public Type Type => _type;

        /// <summary>
        /// Gets the wrapped value.
        /// </summary>
        [JsonIgnore]
        public object Value
        {
            get
            {
                if (_value == null)
                    return null;

                if (_type == null)
                    return _value;

                var valueType = _value.GetType();
                
                if (valueType != _type)
                {
                    // Special handling for other common types that can not be simply cast to the given type
                    if (BsonTypeConversionRegistry.TryGetTypeConversion(_type, out var converter))
                        return converter(_value);

                    // Special handling for enum values, because BSON serializes every integer as Int64!
                    if (_type.IsEnum)
                        return Enum.ToObject(_type, _value);

                    // Special handling for serialized DiffGrams
                    if (_value is SerializedDiffGram serializedDiffGram)
                        return serializedDiffGram.Restore(_type);

                    // Special handling for encodings (Many encodings have its own class e.g. UTF8Encoding)
                    if (typeof(Encoding).IsAssignableFrom(_type))
                        return Encoding.GetEncoding(_value.ToString());

                    // Special handling for values that are serialized to a JObject using a converter (e.g. IPEndPointConverter)
                    if (_value is JObject jObject && _type != typeof(JObject))
                        // TODO: Somewhat ugly and slow but fixes many converters out of the box
                        return jObject.ToObject(_type, JsonSerializer.Create(BsonSerializerAdapter.CurrentSettings));

                    // Fallback to default type conversion (= Convert.ChangeType if not modified)
                    return BsonTypeConversionRegistry.DefaultTypeConversion(_value, _type);
                }

                return _value;
            }
        }
    }
}