using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization.Formatters.Binary;

namespace CoreRemoting.Serialization.Binary
{
    public class BinarySerializerAdapter : ISerializerAdapter
    {
        [ThreadStatic] 
        private static BinaryFormatter _formatter;
        private readonly BinarySerializerConfig _config;

        public bool NeedsKnownTypes => false;

        public BinarySerializerAdapter()
        {
        }
        
        [SuppressMessage("ReSharper", "UnusedMember.Global")]
        public BinarySerializerAdapter(BinarySerializerConfig config)
        {
            _config = config;
        }

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

        public byte[] Serialize<T>(T graph, IEnumerable<Type> knownTypes = null)
        {
            var binaryFormatter = GetFormatter();
            return binaryFormatter.SerializeByteArray(graph);
        }

        public T Deserialize<T>(byte[] rawData, IEnumerable<Type> knownTypes = null)
        {
            var binaryFormatter = GetFormatter();
            return (T)binaryFormatter.DeserializeSafe(rawData);
        }
    }
}