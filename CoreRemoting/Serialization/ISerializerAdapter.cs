using System;
using System.Collections.Generic;

namespace CoreRemoting.Serialization
{
    public interface ISerializerAdapter
    {
        byte[] Serialize<T>(T graph, IEnumerable<Type> knownTypes = null);

        T Deserialize<T>(byte[] rawData, IEnumerable<Type> knownTypes = null);
        
        bool NeedsKnownTypes { get; }
    }
}