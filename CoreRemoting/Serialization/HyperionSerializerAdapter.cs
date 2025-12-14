using System;
using System.IO;
using Hyperion;

namespace CoreRemoting.Serialization
{
	/// <summary>
	/// Serializer adapter for Hyperion binary serializer.
	/// </summary>
	public class HyperionSerializerAdapter : ISerializerAdapter
	{
		private readonly Serializer _serializer;
		private readonly HyperionSerializerConfig _config;

		/// <summary>
		/// Initializes a new instance of the HyperionSerializerAdapter class.
		/// </summary>
		/// <param name="config">Configuration settings for the serializer</param>
		public HyperionSerializerAdapter(HyperionSerializerConfig config = null)
		{
			_config = config ?? new HyperionSerializerConfig();
			
			// Validate configuration
			_config.Validate();

#pragma warning disable CS0618 // Type or member is obsolete
			var options = new SerializerOptions(
				preserveObjectReferences: _config.PreserveObjectReferences,
				ignoreISerializable: _config.IgnoreISerializable,
				versionTolerance: _config.VersionTolerance);
#pragma warning restore CS0618 // Type or member is obsolete

			_serializer = new Serializer(options);
		}

		/// <summary>
		/// Gets whether parameter values must be put in an envelope object for proper deserialization, or not.
		/// Hyperion serializer does not require envelope objects.
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

			// Handle null values - return empty byte array for null
			if (graph == null)
				return Array.Empty<byte>();

			// Check if type is allowed
			if (!_config.IsTypeAllowed(type))
			{
				throw new InvalidOperationException($"Type '{type.FullName}' is not allowed for serialization.");
			}

			try
			{
				using var memoryStream = new MemoryStream();
				
				// Apply compression if enabled
				if (_config.EnableCompression)
				{
					using var compressionStream = new System.IO.Compression.DeflateStream(memoryStream, _config.CompressionLevel, leaveOpen: true);
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

			// Handle null values - empty byte array represents null
			if (rawData.Length == 0)
				return null;

			// Check size limits
			if (rawData.Length > _config.MaxSerializedSize)
			{
				throw new InvalidOperationException($"Serialized data size ({rawData.Length} bytes) exceeds maximum allowed size ({_config.MaxSerializedSize} bytes).");
			}

			// Check if type is allowed
			if (!_config.IsTypeAllowed(type))
			{
				throw new InvalidOperationException($"Type '{type.FullName}' is not allowed for deserialization.");
			}

			try
			{
				using var memoryStream = new MemoryStream(rawData);
				object result;
				
				// Apply decompression if needed
				if (_config.EnableCompression)
				{
					memoryStream.Position = 0;
					using var decompressionStream = new System.IO.Compression.DeflateStream(memoryStream, System.IO.Compression.CompressionMode.Decompress, leaveOpen: true);
					
					// Copy decompressed data to a new memory stream for the serializer
					using var decompressedStream = new MemoryStream();
					decompressionStream.CopyTo(decompressedStream);
					decompressedStream.Position = 0;
					
					result = _serializer.Deserialize(decompressedStream);
				}
				else
				{
					memoryStream.Position = 0;
					result = _serializer.Deserialize(memoryStream);
				}

				// Validate type compatibility
				if (result != null && !type.IsAssignableFrom(result.GetType()))
				{
					throw new InvalidOperationException($"Deserialized object type '{result.GetType().FullName}' is not compatible with expected type '{type.FullName}'.");
				}

				// Check if deserialized type is allowed
				if (!_config.IsTypeAllowed(result.GetType()))
				{
					throw new InvalidOperationException($"Deserialized object type '{result.GetType().FullName}' is not allowed.");
				}

				return result;
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

			var type = typeof(T);
			
			// Check if type is allowed
			if (!_config.IsTypeAllowed(type))
			{
				throw new InvalidOperationException($"Type '{type.FullName}' is not allowed for serialization.");
			}

			try
			{
				if (_config.EnableCompression)
				{
					using var compressionStream = new System.IO.Compression.DeflateStream(stream, _config.CompressionLevel, leaveOpen: true);
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

			// Check if type is allowed
			if (!_config.IsTypeAllowed(type))
			{
				throw new InvalidOperationException($"Type '{type.FullName}' is not allowed for deserialization.");
			}

			try
			{
				object result;
				
				if (_config.EnableCompression)
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

				// Check if deserialized type is allowed
				if (!_config.IsTypeAllowed(result.GetType()))
				{
					throw new InvalidOperationException($"Deserialized object type '{result.GetType().FullName}' is not allowed.");
				}

				return result;
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
		public HyperionSerializerConfig Configuration => _config;
	}
}