using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CoreRemoting.Serialization.Bson.Converters;
using CoreRemoting.Serialization.Bson.Converters.DataSetDiffGramSupport;
using Newtonsoft.Json;
using Newtonsoft.Json.Bson;
using Newtonsoft.Json.Converters;

namespace CoreRemoting.Serialization.Bson
{
    /// <summary>
    /// Serializer adapter to allow BSON serialization.
    /// </summary>
    public class BsonSerializerAdapter : ISerializerAdapter
    {
        private readonly JsonSerializer _serializer;

        internal static JsonSerializerSettings CurrentSettings { get; private set; } = new JsonSerializerSettings();

        /// <summary>
        /// Creates a new instance of the BsonSerializerAdapter class.
        /// </summary>
        /// <param name="config">Optional configuration settings</param>
        public BsonSerializerAdapter(BsonSerializerConfig config = null)
        {
            var settings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.All,
                TypeNameAssemblyFormatHandling = TypeNameAssemblyFormatHandling.Full,
                Formatting = Formatting.Indented,
                ObjectCreationHandling = ObjectCreationHandling.Auto,
                FloatFormatHandling = FloatFormatHandling.String,
                PreserveReferencesHandling = PreserveReferencesHandling.Objects,
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                CheckAdditionalContent = true,
                ConstructorHandling = ConstructorHandling.AllowNonPublicDefaultConstructor,
                DateFormatHandling = DateFormatHandling.IsoDateFormat,
                DateParseHandling = DateParseHandling.DateTimeOffset,
                DateTimeZoneHandling = DateTimeZoneHandling.RoundtripKind,
                DefaultValueHandling = DefaultValueHandling.Include,
                FloatParseHandling = FloatParseHandling.Double,
                ReferenceResolverProvider = () => new BsonReferenceResolver(),
            };

            var converters = new List<JsonConverter>();

            // Add support for DataSet DiffGram serialization and other common types
            if (config == null || config.AddCommonJsonConverters)
            {
                converters.AddRange(
                [
                    new DataSetDiffGramJsonConverter(),
                    new RegionInfoConverter(),
                    new EncodingConverter(),
                    new IPAddressConverter(),
                    new IPEndPointConverter(),
                    new IsoDateTimeConverter(),
                ]);
            }

            if (config != null)
            {
                // Ensure common converters are not added twice
                var existingConverterTypes = new HashSet<Type>(converters.Select(c => c.GetType()));

                // custom converters should have higher priority than common converters
                foreach (var conv in config.JsonConverters)
                    if (!existingConverterTypes.Contains(conv.GetType()))
                        converters.Insert(0, conv);
            }

            settings.Converters = converters;
            
            _serializer = JsonSerializer.Create(settings);
            CurrentSettings = settings;
        }

        /// <summary>
        /// Serializes an object graph.
        /// </summary>
        /// <param name="graph">Object graph to be serialized</param>
        /// <typeparam name="T">Object type</typeparam>
        /// <returns>Serialized data</returns>
        public byte[] Serialize<T>(T graph)
        {
            return Serialize(typeof(T), graph);
        }

        /// <summary>
        /// Serializes an object graph.
        /// </summary>
        /// <param name="type">Object type</param>
        /// <param name="graph">Object graph to be serialized</param>
        /// <returns>Serialized data</returns>
        public byte[] Serialize(Type type, object graph)
        {
            var envelope = new Envelope(graph);
            
            using var stream = new MemoryStream();
            using var writer = new BsonDataWriter(stream);
            _serializer.Serialize(writer, envelope);

            return stream.ToArray();
        }

        /// <summary>
        /// Deserializes raw data back into an object graph.
        /// </summary>
        /// <param name="rawData">Raw data that should be deserialized</param>
        /// <typeparam name="T">Object type</typeparam>
        /// <returns>Deserialized object graph</returns>
        public T Deserialize<T>(byte[] rawData)
        {
            return (T)Deserialize(typeof(T), rawData);
        }

        /// <summary>
        /// Deserializes raw data back into an object graph.
        /// </summary>
        /// <param name="type">Object type</param>
        /// <param name="rawData">Raw data that should be deserialized</param>
        /// <returns>Deserialized object graph</returns>
        public object Deserialize(Type type, byte[] rawData)
        {
            using var stream = new MemoryStream(rawData);
            using var reader = new BsonDataReader(stream);
            var envelope = _serializer.Deserialize<Envelope>(reader);

            var bsonValue = envelope?.Value;
            object value = null;
            
            if (bsonValue != null)
                value = Convert.ChangeType(bsonValue, envelope.Type);

            return value;
        }

        /// <summary>
        /// Gets whether parameter values must be put in an envelope object for proper deserialization, or not. 
        /// </summary>
        public bool EnvelopeNeededForParameterSerialization => true;
    }
}