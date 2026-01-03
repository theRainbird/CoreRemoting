using System;
using System.IO;

namespace CoreRemoting.Serialization.NeoBinary
{
    /// <summary>
    /// Serializer adapter for NeoBinary serialization that implements ISerializerAdapter.
    /// </summary>
    public class NeoBinarySerializerAdapter : ISerializerAdapter
    {
        private readonly NeoBinarySerializer _serializer;

        /// <summary>
        /// Creates a new instance of the NeoBinarySerializerAdapter class.
        /// </summary>
        /// <param name="config">Configuration settings for the serializer</param>
        public NeoBinarySerializerAdapter(NeoBinarySerializerConfig config = null)
        {
            var effectiveConfig = config ?? new NeoBinarySerializerConfig();
            
            var typeValidator = new NeoBinaryTypeValidator()
            {
                AllowUnknownTypes = effectiveConfig.AllowUnknownTypes,
                AllowExpressions = effectiveConfig.AllowExpressions,
                AllowDelegates = effectiveConfig.AllowExpressions, // Allow delegates if expressions are allowed
                AllowReflectionTypes = effectiveConfig.AllowReflectionTypes
            };
            
            // Transfer allowed and blocked types from config to validator
            foreach (var allowedType in effectiveConfig.AllowedTypes)
            {
                typeValidator.AllowType(allowedType);
            }
            
            foreach (var blockedType in effectiveConfig.BlockedTypes)
            {
                typeValidator.BlockType(blockedType);
            }
            
            _serializer = new NeoBinarySerializer
            {
                Config = effectiveConfig,
                TypeValidator = typeValidator
            };

            // Validate configuration
            _serializer.Config.Validate();
        }

        /// <summary>
        /// Gets the underlying NeoBinarySerializer instance.
        /// </summary>
        public NeoBinarySerializer Serializer => _serializer;

        /// <summary>
        /// Gets whether parameter values must be put in an envelope object for proper deserialization, or not.
        /// NeoBinary serializer does not require envelope objects.
        /// </summary>
        public bool EnvelopeNeededForParameterSerialization => false;

        /// <summary>
        /// Serializes an object graph to a byte array.
        /// </summary>
        /// <typeparam name="T">Object type</typeparam>
        /// <param name="graph">Object graph to be serialized</param>
        /// <returns>Serialized data</returns>
        public byte[] Serialize<T>(T graph)
        {
            return Serialize(typeof(T), graph);
        }

        /// <summary>
        /// Serializes an object graph to a byte array.
        /// </summary>
        /// <param name="type">Object type</param>
        /// <param name="graph">Object graph to be serialized</param>
        /// <returns>Serialized data</returns>
        public byte[] Serialize(Type type, object graph)
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type));

            try
            {
                using var memoryStream = new MemoryStream();
                
                // Apply compression if enabled
                if (_serializer.Config.EnableCompression)
                {
                    using var compressionStream = new System.IO.Compression.DeflateStream(memoryStream, _serializer.Config.CompressionLevel, leaveOpen: true);
                    _serializer.Serialize(graph, compressionStream);
                    compressionStream.Close();
                }
                else
                {
                    _serializer.Serialize(graph, memoryStream);
                }

                return memoryStream.ToArray();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to serialize object of type '{type.FullName}'. See inner exception for details.", ex);
            }
        }

        /// <summary>
        /// Deserializes raw data back into an object graph.
        /// </summary>
        /// <typeparam name="T">Object type</typeparam>
        /// <param name="rawData">Raw data that should be deserialized</param>
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
            if (type == null)
                throw new ArgumentNullException(nameof(type));
            if (rawData == null)
                throw new ArgumentNullException(nameof(rawData));

            // Check size limits
            if (rawData.Length > _serializer.Config.MaxSerializedSize)
            {
                throw new InvalidOperationException($"Serialized data size ({rawData.Length} bytes) exceeds maximum allowed size ({_serializer.Config.MaxSerializedSize} bytes).");
            }

            try
            {
                using var memoryStream = new MemoryStream(rawData);
                
                // Apply decompression if needed
                if (_serializer.Config.EnableCompression)
                {
                    memoryStream.Position = 0;
                    using var decompressionStream = new System.IO.Compression.DeflateStream(memoryStream, System.IO.Compression.CompressionMode.Decompress, leaveOpen: true);
                    
                    // Copy decompressed data to a new memory stream for the serializer
                    using var decompressedStream = new MemoryStream();
                    decompressionStream.CopyTo(decompressedStream);
                    decompressedStream.Position = 0;
                    
                    var result = _serializer.Deserialize(decompressedStream);
                    
                    // Validate type compatibility
                    if (result != null && !type.IsAssignableFrom(result.GetType()))
                    {
                        throw new InvalidOperationException($"Deserialized object type '{result.GetType().FullName}' is not compatible with expected type '{type.FullName}'.");
                    }
                    
                    return result;
                }
                else
                {
                    memoryStream.Position = 0;
                    var result = _serializer.Deserialize(memoryStream);
                    
                    // Validate type compatibility
                    if (result != null && !type.IsAssignableFrom(result.GetType()))
                    {
                        throw new InvalidOperationException($"Deserialized object type '{result.GetType().FullName}' is not compatible with expected type '{type.FullName}'.");
                    }
                    
                    return result;
                }
            }
            catch (NeoBinaryUnsafeDeserializationException)
            {
                // Re-throw security exceptions as-is
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to deserialize data to type '{type.FullName}'. See inner exception for details.", ex);
            }
        }

        /// <summary>
        /// Serializes an object to a stream.
        /// </summary>
        /// <typeparam name="T">Object type</typeparam>
        /// <param name="graph">Object to serialize</param>
        /// <param name="stream">Stream to write to</param>
        public void SerializeToStream<T>(T graph, Stream stream)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            try
            {
                if (_serializer.Config.EnableCompression)
                {
                    using var compressionStream = new System.IO.Compression.DeflateStream(stream, _serializer.Config.CompressionLevel, leaveOpen: true);
                    _serializer.Serialize(graph, compressionStream);
                    compressionStream.Close();
                }
                else
                {
                    _serializer.Serialize(graph, stream);
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to serialize object of type '{typeof(T).FullName}' to stream. See inner exception for details.", ex);
            }
        }

        /// <summary>
        /// Deserializes an object from a stream.
        /// </summary>
        /// <typeparam name="T">Object type</typeparam>
        /// <param name="stream">Stream to read from</param>
        /// <returns>Deserialized object</returns>
        public T DeserializeFromStream<T>(Stream stream)
        {
            return (T)DeserializeFromStream(typeof(T), stream);
        }

        /// <summary>
        /// Deserializes an object from a stream.
        /// </summary>
        /// <param name="type">Object type</param>
        /// <param name="stream">Stream to read from</param>
        /// <returns>Deserialized object</returns>
        public object DeserializeFromStream(Type type, Stream stream)
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type));
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            try
            {
                object result;
                
                if (_serializer.Config.EnableCompression)
                {
                    using var compressionStream = new System.IO.Compression.DeflateStream(stream, System.IO.Compression.CompressionMode.Decompress);
                    result = _serializer.Deserialize(compressionStream);
                }
                else
                {
                    result = _serializer.Deserialize(stream);
                }

                // Validate type compatibility
                if (result != null && !type.IsAssignableFrom(result.GetType()))
                {
                    throw new InvalidOperationException($"Deserialized object type '{result.GetType().FullName}' is not compatible with expected type '{type.FullName}'.");
                }

                return result;
            }
            catch (NeoBinaryUnsafeDeserializationException)
            {
                // Re-throw security exceptions as-is
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to deserialize data from stream to type '{type.FullName}'. See inner exception for details.", ex);
            }
        }

        /// <summary>
        /// Creates a clone of an object using serialization.
        /// </summary>
        /// <typeparam name="T">Object type</typeparam>
        /// <param name="obj">Object to clone</param>
        /// <returns>Cloned object</returns>
        public T Clone<T>(T obj)
        {
            if (obj == null)
                return default(T);

            var serialized = Serialize(obj);
            return Deserialize<T>(serialized);
        }

        /// <summary>
        /// Gets the configuration of the underlying serializer.
        /// </summary>
        public NeoBinarySerializerConfig Configuration => _serializer.Config;

        /// <summary>
        /// Gets the type validator of the underlying serializer.
        /// </summary>
        public NeoBinaryTypeValidator TypeValidator => _serializer.TypeValidator;
    }
}