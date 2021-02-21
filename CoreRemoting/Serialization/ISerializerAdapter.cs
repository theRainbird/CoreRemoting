using System;

namespace CoreRemoting.Serialization
{
    /// <summary>
    /// Interface that serializer adapter components must implement.
    /// </summary>
    public interface ISerializerAdapter
    {
        /// <summary>
        /// Gets whether parameter values must be put in an envelope object for proper deserialization, or not. 
        /// </summary>
        bool EnvelopeNeededForParameterSerialization { get; }
        
        /// <summary>
        /// Serializes an object graph.
        /// </summary>
        /// <param name="graph">Object graph to be serialized</param>
        /// <typeparam name="T">Object type</typeparam>
        /// <returns>Serialized data</returns>
        byte[] Serialize<T>(T graph);
        
        /// <summary>
        /// Serializes an object graph.
        /// </summary>
        /// <param name="type">Object type</param>
        /// <param name="graph">Object graph to be serialized</param>
        /// <returns>Serialized data</returns>
        byte[] Serialize(Type type, object graph);

        /// <summary>
        /// Deserializes raw data back into an object graph.
        /// </summary>
        /// <param name="rawData">Raw data that should be deserialized</param>
        /// <typeparam name="T">Object type</typeparam>
        /// <returns>Deserialized object graph</returns>
        T Deserialize<T>(byte[] rawData);
        
        /// <summary>
        /// Deserializes raw data back into an object graph.
        /// </summary>
        /// <param name="type">Object type</param>
        /// <param name="rawData">Raw data that should be deserialized</param>
        /// <returns>Deserialized object graph</returns>
        object Deserialize(Type type, byte[] rawData);
    }
}