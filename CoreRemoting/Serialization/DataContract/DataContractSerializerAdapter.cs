using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Xml;

namespace CoreRemoting.Serialization.DataContract
{
    public class DataContractSerializerAdapter : ISerializerAdapter
    {
        private readonly DataContractSerializerSettings _config;
        private readonly Encoding _encoding;
        
        public DataContractSerializerAdapter() : this(new DataContractSerializerConfig())
        {
        }

        public bool NeedsKnownTypes => true;

        public DataContractSerializerAdapter(DataContractSerializerConfig config)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            _encoding = config.Encoding;
            _config = config.ToDataContractSerializerSettings();
        }

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