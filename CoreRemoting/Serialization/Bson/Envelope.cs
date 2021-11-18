using System;
using System.Diagnostics.CodeAnalysis;
using Newtonsoft.Json;

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
        public Type Type => _type;

        /// <summary>
        /// Gets the wrapped value.
        /// </summary>
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

                    return Convert.ChangeType(_value, _type);
                }

                return _value;
            }
        }
    }
}