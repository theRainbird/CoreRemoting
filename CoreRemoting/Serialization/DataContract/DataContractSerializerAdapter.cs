using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Xml;

namespace CoreRemoting.Serialization.DataContract
{
    /// <summary>
    /// Serializer adapter to allow data contract serialization.
    /// </summary>
    public class DataContractSerializerAdapter : ISerializerAdapter
    {
        private readonly DataContractSerializerSettings _config;
        private readonly Encoding _encoding;
        
        /// <summary>
        /// Creates a new instance of the DataContractSerializerAdapter class.
        /// </summary>
        public DataContractSerializerAdapter() : this(new DataContractSerializerConfig())
        {
        }

        /// <summary>
        /// Get whether this serialization adapter needs known types to be specified.
        /// </summary>
        public bool NeedsKnownTypes => true;

        /// <summary>
        /// Creates a new instance of the DataContractSerializerAdapter class.
        /// </summary>
        /// <param name="config">Configuration settings</param>
        [SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
        public DataContractSerializerAdapter(DataContractSerializerConfig config)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            _encoding = config.Encoding;
            _config = config.ToDataContractSerializerSettings();
        }

        /// <summary>
        /// Serializes an object graph.
        /// </summary>
        /// <param name="graph">Object graph to be serialized</param>
        /// <param name="knownTypes">Optional list of known types</param>
        /// <typeparam name="T">Object type</typeparam>
        /// <returns>Serialized data</returns>
        public byte[] Serialize<T>(T graph, IEnumerable<Type> knownTypes = null)
        {
            UpdateKnownTypesInConfig(knownTypes);

            var xmlSerializer = new DataContractSerializer(typeof(T), _config);
            
            using var stream = new MemoryStream();
            using var xmlTextWriter =
                XmlWriter.Create(
                    output: stream,
                    settings: new XmlWriterSettings()
                    {
                        Encoding = _encoding,
                        Indent = true
                    });
            
            xmlSerializer.WriteObject(xmlTextWriter, graph);
            
            xmlTextWriter.Flush();

            return stream.ToArray();
        }

        /// <summary>
        /// Updates known types in configuration.
        /// </summary>
        /// <param name="knownTypes">List of known types that should be added to configuration</param>
        private void UpdateKnownTypesInConfig(IEnumerable<Type> knownTypes)
        {
            if (knownTypes != null)
            {
                _config.KnownTypes ??= new List<Type>();
                
                var knownTypeList = knownTypes.ToList();
                if (knownTypeList.Any())
                    _config.KnownTypes = 
                        _config.KnownTypes.Union(knownTypeList.Except(_config.KnownTypes));
            }
        }

        /// <summary>
        /// Deserializes raw data back into an object graph.
        /// </summary>
        /// <param name="rawData">Raw data that should be deserialized</param>
        /// <param name="knownTypes">Optional list of known types</param>
        /// <typeparam name="T">Object type</typeparam>
        /// <returns>Deserialized object graph</returns>
        public T Deserialize<T>(byte[] rawData, IEnumerable<Type> knownTypes = null)
        {
            UpdateKnownTypesInConfig(knownTypes);
            
            var xmlSerializer = new DataContractSerializer(typeof(T), _config);

            MemoryStream trimmedStream;
            
            using (var stream = new MemoryStream(rawData))
            {
                // Remove trailing NULL chars, which may be present when message encryption is on
                string xml = _encoding.GetString(stream.ToArray()).TrimEnd((char) 0x00);
                trimmedStream = new MemoryStream(_encoding.GetBytes(xml));
            }

            var deserializedObject = xmlSerializer.ReadObject(trimmedStream);

            trimmedStream.Close();
            
            return (T)deserializedObject;
        }
    }
}