using System;
using System.Collections.Generic;

namespace CoreRemoting.Serialization.NeoBinary
{
	/// <summary>
	/// Configuration settings for NeoBinary serializer.
	/// </summary>
		public class NeoBinarySerializerConfig
		{
		/// <summary>
		/// Creates a new instance of the NeoBinarySerializerConfig class.
		/// </summary>
			public NeoBinarySerializerConfig()
			{
				IncludeAssemblyVersions = false;
				UseTypeReferences = true;
				MaxObjectGraphDepth = 100;
				MaxSerializedSize = 100 * 1024 * 1024; // 100MB
				AllowedTypes = new HashSet<Type>();
				BlockedTypes = new HashSet<Type>();
				AllowUnknownTypes = true;
				UseIlCompactLayout = true;
			}

		/// <summary>
		/// Gets or sets whether assembly versions should be included in type metadata.
		/// </summary>
		public bool IncludeAssemblyVersions { get; set; }

		/// <summary>
		/// Gets or sets whether type references should be used for circular reference handling.
		/// </summary>
		public bool UseTypeReferences { get; set; }

		/// <summary>
		/// Gets or sets the maximum depth of object graph to prevent stack overflow.
		/// </summary>
		public int MaxObjectGraphDepth { get; set; }

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
		public bool AllowExpressions { get; set; } = false;

		/// <summary>
		/// Gets or sets whether reflection types should be allowed during serialization/deserialization.
		/// When true, types like MethodInfo, PropertyInfo, FieldInfo, etc. are supported.
		/// When false, reflection types are blocked for security reasons.
		/// </summary>
		public bool AllowReflectionTypes { get; set; } = true;

		/// <summary>
		/// Gets or sets whether to include field names during serialization.
		/// When true, field names are included for better compatibility.
		/// </summary>
		public bool IncludeFieldNames { get; set; } = true;

		/// <summary>
		/// Enables the IL compact layout for complex objects: no field names/count in the payload, fixed field order.
		/// When enabled, the serializer writes a compact subformat tag (0xFE) after TypeInfo and uses
		/// specialized IL readers/writers for fields. This significantly improves throughput for complex graphs.
		/// Sender and receiver must both enable this flag for the same stream.
		/// </summary>
		public bool UseIlCompactLayout { get; set; }

		/// <summary>
		/// Gets or sets whether to compress serialized data.
		/// </summary>
		public bool EnableCompression { get; set; } = false;

		/// <summary>
		/// Gets or sets the compression level when compression is enabled.
		/// </summary>
		public System.IO.Compression.CompressionLevel CompressionLevel { get; set; } =
			System.IO.Compression.CompressionLevel.Optimal;

		/// <summary>
		/// Gets or sets whether to use binary serialization for DataSets and DataTables instead of XML.
		/// Binary serialization is faster but may not be compatible with all DataSet schemas.
		/// </summary>
		public bool EnableBinaryDataSetSerialization { get; set; } = false;

		/// <summary>
		/// Creates a copy of this configuration.
		/// </summary>
		/// <returns>A new NeoBinarySerializerConfig instance with same settings</returns>
		public NeoBinarySerializerConfig Clone()
		{
			return new NeoBinarySerializerConfig
			{
				IncludeAssemblyVersions = this.IncludeAssemblyVersions,
				UseTypeReferences = this.UseTypeReferences,
				MaxObjectGraphDepth = this.MaxObjectGraphDepth,
				MaxSerializedSize = this.MaxSerializedSize,
				AllowedTypes = new HashSet<Type>(this.AllowedTypes),
				BlockedTypes = new HashSet<Type>(this.BlockedTypes),
				AllowUnknownTypes = this.AllowUnknownTypes,
				IncludeFieldNames = this.IncludeFieldNames,
				EnableCompression = this.EnableCompression,
				CompressionLevel = this.CompressionLevel,
				EnableBinaryDataSetSerialization = this.EnableBinaryDataSetSerialization,
				AllowExpressions = this.AllowExpressions,
				AllowReflectionTypes = this.AllowReflectionTypes,
				UseIlCompactLayout = this.UseIlCompactLayout
			};
		}

		/// <summary>
		/// Validates the configuration settings.
		/// </summary>
		/// <exception cref="ArgumentException">Thrown when configuration is invalid</exception>
		public void Validate()
		{
			if (MaxObjectGraphDepth <= 0)
				throw new ArgumentException("MaxObjectGraphDepth must be greater than 0");

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