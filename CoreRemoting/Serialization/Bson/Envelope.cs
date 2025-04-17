using System;
using System.Diagnostics.CodeAnalysis;
using CoreRemoting.Serialization.Bson.DataSetDiffGramSupport;
using Newtonsoft.Json;

namespace CoreRemoting.Serialization.Bson
{
    /// <summary>
    /// Wraps values and preserve their types for serialization.
    /// </summary>
    [SuppressMessage("ReSharper", "ConvertToAutoProperty")]
    public class Envelope
    {
        /// <summary>
        /// Gets or sets the function used to convert a wrapped value to its expected type.
        /// </summary>
        /// <remarks>
        /// This strategy is invoked when the actual type of the wrapped value differs from the expected type.
        /// By default, it uses <see cref="Convert.ChangeType(object, Type)"/>, but it can be overridden
        /// to provide custom conversion logic for edge cases or unsupported types.
        /// </remarks>
        public static Func<object, Type, object> TypeConversionStrategy { get; set; } = Convert.ChangeType;

        [JsonProperty]
        private object _value;
        
        [JsonProperty]
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
                    // Special handling for enum values, because BSON serializes every integer as Int64!
                    if (_type.IsEnum && valueType != _type)
                        return Enum.ToObject(_type, _value);
                    
                    // Special handling for serializes DiffGrams
                    if (_value.GetType() == typeof(SerializedDiffGram))
                    {
                        var serializedDiffGram = (SerializedDiffGram)_value;
                        return serializedDiffGram.Restore(_type);
                    }

                    // Special handling of TimeSpan values, because BSON serializes TimeSpans as strings
                    if (_type == typeof(TimeSpan))
                        return TimeSpan.Parse(_value.ToString());

                    return TypeConversionStrategy(_value, _type);
                }

                return _value;
            }
        }
    }
}