using System;
using System.Collections.Generic;

namespace CoreRemoting.Serialization
{
	/// <summary>
	/// Configuration settings for Hyperion serializer.
	/// </summary>
	public class HyperionSerializerConfig
	{
		/// <summary>
		/// Creates a new instance of the HyperionSerializerConfig class.
		/// </summary>
		public HyperionSerializerConfig()
		{
			PreserveObjectReferences = true;
			IgnoreISerializable = false;
			VersionTolerance = true;
			MaxSerializedSize = 100 * 1024 * 1024; // 100MB
			AllowedTypes = new HashSet<Type>();
			BlockedTypes = new HashSet<Type>();
			AllowUnknownTypes = true;
			AllowExpressions = false;
			EnableCompression = false;
			CompressionLevel = System.IO.Compression.CompressionLevel.Optimal;
		}

		/// <summary>
		/// Gets or sets whether object references should be preserved for circular reference handling.
		/// </summary>
		public bool PreserveObjectReferences { get; set; }

		/// <summary>
		/// Gets or sets whether ISerializable interface should be ignored.
		/// </summary>
		public bool IgnoreISerializable { get; set; }

		/// <summary>
		/// Gets or sets whether version tolerance should be enabled for compatibility across versions.
		/// </summary>
		public bool VersionTolerance { get; set; }

		/// <summary>
		/// Gets or sets the maximum allowed serialized data size in bytes.
		/// </summary>
		public long MaxSerializedSize { get; set; }

		/// <summary>
		/// Gets or sets the set of explicitly allowed types.
		/// If this set is not empty, only these types can be deserialized.
		/// </summary>
		public HashSet<Type> AllowedTypes { get; set; }

		/// <summary>
		/// Gets or sets the set of explicitly blocked types.
		/// These types cannot be deserialized even if they would otherwise be allowed.
		/// </summary>
		public HashSet<Type> BlockedTypes { get; set; }

		/// <summary>
		/// Gets or sets whether unknown types should be allowed during deserialization.
		/// When false, only known and allowed types can be deserialized.
		/// </summary>
		public bool AllowUnknownTypes { get; set; }

		/// <summary>
		/// Gets or sets whether LINQ expressions should be allowed during serialization/deserialization.
		/// When false, expressions are not supported for security reasons.
		/// </summary>
		public bool AllowExpressions { get; set; }

		/// <summary>
		/// Gets or sets whether to compress serialized data.
		/// </summary>
		public bool EnableCompression { get; set; }

		/// <summary>
		/// Gets or sets the compression level when compression is enabled.
		/// </summary>
		public System.IO.Compression.CompressionLevel CompressionLevel { get; set; }

		/// <summary>
		/// Creates a copy of this configuration.
		/// </summary>
		/// <returns>A new HyperionSerializerConfig instance with the same settings</returns>
		public HyperionSerializerConfig Clone()
		{
			return new HyperionSerializerConfig
			{
				PreserveObjectReferences = this.PreserveObjectReferences,
				IgnoreISerializable = this.IgnoreISerializable,
				VersionTolerance = this.VersionTolerance,
				MaxSerializedSize = this.MaxSerializedSize,
				AllowedTypes = new HashSet<Type>(this.AllowedTypes),
				BlockedTypes = new HashSet<Type>(this.BlockedTypes),
				AllowUnknownTypes = this.AllowUnknownTypes,
				AllowExpressions = this.AllowExpressions,
				EnableCompression = this.EnableCompression,
				CompressionLevel = this.CompressionLevel
			};
		}

		/// <summary>
		/// Validates the configuration settings.
		/// </summary>
		/// <exception cref="ArgumentException">Thrown when configuration is invalid</exception>
		public void Validate()
		{
			if (MaxSerializedSize <= 0)
				throw new ArgumentException("MaxSerializedSize must be greater than 0");

			// Check for conflicts between allowed and blocked types
			foreach (var blockedType in BlockedTypes)
			{
				if (AllowedTypes.Contains(blockedType))
				{
					throw new ArgumentException($"Type '{blockedType.FullName}' cannot be both allowed and blocked");
				}
			}
		}

		/// <summary>
		/// Adds a type to the allowed types list.
		/// </summary>
		/// <typeparam name="T">Type to allow</typeparam>
		public void AllowType<T>()
		{
			AllowedTypes.Add(typeof(T));
		}

		/// <summary>
		/// Adds a type to the blocked types list.
		/// </summary>
		/// <typeparam name="T">Type to block</typeparam>
		public void BlockType<T>()
		{
			BlockedTypes.Add(typeof(T));
		}

		/// <summary>
		/// Removes a type from the allowed types list.
		/// </summary>
		/// <typeparam name="T">Type to remove from allowed list</typeparam>
		public void RemoveAllowedType<T>()
		{
			AllowedTypes.Remove(typeof(T));
		}

		/// <summary>
		/// Removes a type from the blocked types list.
		/// </summary>
		/// <typeparam name="T">Type to remove from blocked list</typeparam>
		public void RemoveBlockedType<T>()
		{
			BlockedTypes.Remove(typeof(T));
		}

		/// <summary>
		/// Checks if a type is allowed according to the current configuration.
		/// </summary>
		/// <param name="type">Type to check</param>
		/// <returns>True if the type is allowed, false otherwise</returns>
		public bool IsTypeAllowed(Type type)
		{
			if (BlockedTypes.Contains(type))
				return false;

			if (AllowedTypes.Count == 0)
				return AllowUnknownTypes;

			return AllowedTypes.Contains(type);
		}
	}
}