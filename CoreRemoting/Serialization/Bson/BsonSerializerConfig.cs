using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Newtonsoft.Json;

namespace CoreRemoting.Serialization.Bson
{
    /// <summary>
    /// Describes BSON serializer settings.
    /// </summary>
    [SuppressMessage("ReSharper", "ClassNeverInstantiated.Global")]
    public class BsonSerializerConfig
    {
        /// <summary>
        /// Creates a new instance of the BsonSerializerConfig class.
        /// </summary>
        /// <param name="jsonConverters">Optional list of JSON converters</param>
        public BsonSerializerConfig(IEnumerable<JsonConverter> jsonConverters = null)
        {
            JsonConverters = new List<JsonConverter>();
            
            if (jsonConverters!=null)
                JsonConverters.AddRange(jsonConverters);
        }
        
        /// <summary>
        /// Gets a list of JSON converters to customize BSON serialization.
        /// </summary>
        public List<JsonConverter> JsonConverters { get; }
    }
}