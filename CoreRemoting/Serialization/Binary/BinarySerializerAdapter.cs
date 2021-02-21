using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization.Formatters.Binary;

namespace CoreRemoting.Serialization.Binary
{
    /// <summary>
    /// Serializer adapter to allow binary serialization.
    /// </summary>
    public class BinarySerializerAdapter : ISerializerAdapter
    {
        [ThreadStatic] 
        private static BinaryFormatter _formatter;
        private readonly BinarySerializerConfig _config;
       
        /// <summary>
        /// Creates a new instance of the BinarySerializerAdapter class.
        /// </summary>
        /// <param name="config">Configuration settings</param>
        [SuppressMessage("ReSharper", "UnusedMember.Global")]
        public BinarySerializerAdapter(BinarySerializerConfig config = null)
        {
            _config = config;
        }

        /// <summary>
        /// Gets a formatter instance.
        /// The instance is reused for further calls.
        /// </summary>
        /// <returns>Binary formatter instance</returns>
        private BinaryFormatter GetFormatter()
        {
            if (_formatter == null)
            {
                _formatter = new BinaryFormatter();

                if (_config != null)
                {
                    _formatter.TypeFormat = _config.TypeFormat;
                    _formatter.FilterLevel = _config.FilterLevel;
                    _formatter.AssemblyFormat = 
                        _config.SerializeAssemblyVersions 
                            ? System.Runtime.Serialization.Formatters.FormatterAssemblyStyle.Full 
                            : System.Runtime.Serialization.Formatters.FormatterAssemblyStyle.Simple;
                }
            }

            return _formatter;
        }

        /// <summary>
        /// Serializes an object graph.
        /// </summary>
        /// <param name="graph">Object graph to be serialized</param>
        /// <typeparam name="T">Object type</typeparam>
        /// <returns>Serialized data</returns>
        public byte[] Serialize<T>(T graph)
        {
            var binaryFormatter = GetFormatter();
            return binaryFormatter.SerializeByteArray(graph);
        }

        /// <summary>
        /// Serializes an object graph.
        /// </summary>
        /// <param name="type">Object type</param>
        /// <param name="graph">Object graph to be serialized</param>
        /// <returns>Serialized data</returns>
        public byte[] Serialize(Type type, object graph)
        {
            var binaryFormatter = GetFormatter();
            return binaryFormatter.SerializeByteArray(graph);
        }

        /// <summary>
        /// Deserializes raw data back into an object graph.
        /// </summary>
        /// <param name="rawData">Raw data that should be deserialized</param>
        /// <typeparam name="T">Object type</typeparam>
        /// <returns>Deserialized object graph</returns>
        public T Deserialize<T>(byte[] rawData)
        {
            var binaryFormatter = GetFormatter();
            return (T)binaryFormatter.DeserializeSafe(rawData);
        }

        /// <summary>
        /// Deserializes raw data back into an object graph.
        /// </summary>
        /// <param name="type">Object type</param>
        /// <param name="rawData">Raw data that should be deserialized</param>
        /// <returns>Deserialized object graph</returns>
        public object Deserialize(Type type, byte[] rawData)
        {
            var binaryFormatter = GetFormatter();
            return binaryFormatter.DeserializeSafe(rawData);
        }
        
        /// <summary>
        /// Gets whether parameter values must be put in an envelope object for proper deserialization, or not. 
        /// </summary>
        public bool EnvelopeNeededForParameterSerialization => false;
    }
}