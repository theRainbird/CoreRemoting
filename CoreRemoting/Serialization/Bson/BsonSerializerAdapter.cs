using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Bson;

namespace CoreRemoting.Serialization.Bson
{
    /// <summary>
    /// Serializer adapter to allow BSON serialization.
    /// </summary>
    public class BsonSerializerAdapter : ISerializerAdapter
    {
        private JsonSerializerSettings _settings;

        /// <summary>
        /// Creates a new instance of the BsonSerializerAdapter class.
        /// </summary>
        public BsonSerializerAdapter()
        {
            _settings = new JsonSerializerSettings()
            {
                TypeNameHandling = TypeNameHandling.All,
                TypeNameAssemblyFormatHandling = TypeNameAssemblyFormatHandling.Full,
                Formatting = Formatting.Indented,
                ObjectCreationHandling = ObjectCreationHandling.Auto,
                FloatFormatHandling = FloatFormatHandling.String,
                PreserveReferencesHandling = PreserveReferencesHandling.Objects,
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                CheckAdditionalContent = true
            };
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
            
            var serializer = JsonSerializer.Create(_settings);
            
            using var stream = new MemoryStream();
            using var writer = new BsonDataWriter(stream);
            serializer.Serialize(writer, envelope);

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
            var serializer = JsonSerializer.Create(_settings);
            
            using var stream = new MemoryStream(rawData);
            using var reader = new BsonDataReader(stream);
            var envelope = serializer.Deserialize<Envelope>(reader);

            var bsonValue = envelope?.Value;
            object value = null;
            
            if (bsonValue != null)
                value = Convert.ChangeType(bsonValue, envelope.Type);

            return value;
        }
    }
}