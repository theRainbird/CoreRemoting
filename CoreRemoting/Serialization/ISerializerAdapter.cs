using System;
using System.Collections.Generic;

namespace CoreRemoting.Serialization
{
    /// <summary>
    /// Interface that serializer adapter components must implement.
    /// </summary>
    public interface ISerializerAdapter
    {
        /// <summary>
        /// Serializes an object graph.
        /// </summary>
        /// <param name="graph">Object graph to be serialized</param>
        /// <param name="knownTypes">Optional list of known types</param>
        /// <typeparam name="T">Object type</typeparam>
        /// <returns>Serialized data</returns>
        byte[] Serialize<T>(T graph, IEnumerable<Type> knownTypes = null);

        /// <summary>
        /// Deserializes raw data back into an object graph.
        /// </summary>
        /// <param name="rawData">Raw data that should be deserialized</param>
        /// <param name="knownTypes">Optional list of known types</param>
        /// <typeparam name="T">Object type</typeparam>
        /// <returns>Deserialized object graph</returns>
        T Deserialize<T>(byte[] rawData, IEnumerable<Type> knownTypes = null);
        
        /// <summary>
        /// Get whether this serialization adapter needs known types to be specified.
        /// </summary>
        bool NeedsKnownTypes { get; }
    }
}