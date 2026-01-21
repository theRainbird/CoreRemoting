using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace CoreRemoting.Serialization.NeoBinary;

/// <summary>
/// Serializer adapter for NeoBinary serialization that implements ISerializerAdapter.
/// </summary>
public class NeoBinarySerializerAdapter : ISerializerAdapter
{
	/// <summary>
	/// Creates a new instance of the NeoBinarySerializerAdapter class.
	/// </summary>
	/// <param name="config">Configuration settings for the serializer</param>
	public NeoBinarySerializerAdapter(NeoBinarySerializerConfig config = null)
	{
		var effectiveConfig = config ?? new NeoBinarySerializerConfig();

		var typeValidator = new NeoBinaryTypeValidator
		{
			AllowUnknownTypes = effectiveConfig.AllowUnknownTypes,
			AllowExpressions = effectiveConfig.AllowExpressions,
			AllowDelegates = effectiveConfig.AllowExpressions, // Allow delegates if expressions are allowed
			AllowReflectionTypes = effectiveConfig.AllowReflectionTypes
		};

		// Transfer allowed and blocked types from config to validator
		foreach (var allowedType in effectiveConfig.AllowedTypes) typeValidator.AllowType(allowedType);

		foreach (var blockedType in effectiveConfig.BlockedTypes) typeValidator.BlockType(blockedType);

		Serializer = new NeoBinarySerializer
		{
			Config = effectiveConfig,
			TypeValidator = typeValidator
		};

		// Validate configuration
		Serializer.Config.Validate();
	}

	/// <summary>
	/// Gets the underlying NeoBinarySerializer instance.
	/// </summary>
	public NeoBinarySerializer Serializer { get; }

	/// <summary>
	/// Gets comprehensive serializer cache statistics.
	/// </summary>
	/// <returns>Cache statistics</returns>
	public SerializerCache.CacheStatistics GetCacheStatistics()
	{
		return Serializer.GetCacheStatistics();
	}

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
			if (Serializer.Config.EnableCompression)
			{
				using var compressionStream =
					new System.IO.Compression.DeflateStream(memoryStream, Serializer.Config.CompressionLevel, true);
				Serializer.Serialize(graph, compressionStream);
				compressionStream.Close();
			}
			else
			{
				Serializer.Serialize(graph, memoryStream);
			}

			return memoryStream.ToArray();
		}
		catch (Exception ex)
		{
			throw new InvalidOperationException(
				$"Failed to serialize object of type '{type.FullName}'. See inner exception for details.", ex);
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
		if (rawData.Length > Serializer.Config.MaxSerializedSize)
			throw new InvalidOperationException(
				$"Serialized data size ({rawData.Length} bytes) exceeds maximum allowed size ({Serializer.Config.MaxSerializedSize} bytes).");

		try
		{
			using var memoryStream = new MemoryStream(rawData);

			// Apply decompression if needed
			if (Serializer.Config.EnableCompression)
			{
				memoryStream.Position = 0;
				using var decompressionStream = new System.IO.Compression.DeflateStream(memoryStream,
					System.IO.Compression.CompressionMode.Decompress, true);

				// Copy decompressed data to a new memory stream for the serializer
				using var decompressedStream = new MemoryStream();
				decompressionStream.CopyTo(decompressedStream);
				decompressedStream.Position = 0;

				var result = Serializer.Deserialize(decompressedStream);

				// Validate type compatibility
				if (result != null && !type.IsAssignableFrom(result.GetType()))
					throw new InvalidOperationException(
						$"Deserialized object type '{result.GetType().FullName}' is not compatible with expected type '{type.FullName}'.");

				return result;
			}
			else
			{
				memoryStream.Position = 0;
				var result = Serializer.Deserialize(memoryStream);

				// Validate type compatibility
				if (result != null && !type.IsAssignableFrom(result.GetType()))
					throw new InvalidOperationException(
						$"Deserialized object type '{result.GetType().FullName}' is not compatible with expected type '{type.FullName}'.");

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
			throw new InvalidOperationException(
				$"Failed to deserialize data to type '{type.FullName}'. See inner exception for details.", ex);
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
			return default;

		var serialized = Serialize(obj);
		return Deserialize<T>(serialized);
	}

	/// <summary>
	/// Gets the configuration of the underlying serializer.
	/// </summary>
	[SuppressMessage("ReSharper", "UnusedMember.Global")]
	public NeoBinarySerializerConfig Configuration => Serializer.Config;

	/// <summary>
	/// Gets the type validator of the underlying serializer.
	/// </summary>
	[SuppressMessage("ReSharper", "UnusedMember.Global")]
	public NeoBinaryTypeValidator TypeValidator => Serializer.TypeValidator;
}