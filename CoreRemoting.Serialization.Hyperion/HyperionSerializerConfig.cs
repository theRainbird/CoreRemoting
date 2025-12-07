using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;

namespace CoreRemoting.Serialization.Hyperion
{
	/// <summary>
	/// Configuration settings for Hyperion serializer.
	/// </summary>
	public class HyperionSerializerConfig
	{
		private readonly HashSet<string> _allowedTypes = new HashSet<string>();
		private readonly HashSet<string> _allowedNamespaces = new HashSet<string>();
		private readonly HashSet<string> _blockedTypes = new HashSet<string>();
		private readonly HashSet<string> _blockedNamespaces = new HashSet<string>();

		/// <summary>
		/// Gets or sets whether object references should be preserved.
		/// Default: true
		/// </summary>
		public bool PreserveObjectReferences { get; set; } = true;

		/// <summary>
		/// Gets or sets whether ISerializable interface should be ignored.
		/// Default: false
		/// </summary>
		public bool IgnoreISerializable { get; set; } = false;

		/// <summary>
		/// Gets or sets whether version tolerance should be enabled.
		/// Default: true
		/// </summary>
		public bool VersionTolerance { get; set; } = true;

		/// <summary>
		/// Gets or sets whether compression should be enabled.
		/// Default: false
		/// </summary>
		public bool EnableCompression { get; set; } = false;

		/// <summary>
		/// Gets or sets the compression level when compression is enabled.
		/// Default: Optimal
		/// </summary>
		public CompressionLevel CompressionLevel { get; set; } = CompressionLevel.Optimal;

		/// <summary>
		/// Gets or sets the maximum allowed serialized data size in bytes.
		/// Default: 100 MB
		/// </summary>
		public long MaxSerializedSize { get; set; } = 100 * 1024 * 1024;

		/// <summary>
		/// Gets or sets whether type safety should be enforced.
		/// Default: true
		/// </summary>
		public bool EnforceTypeSafety { get; set; } = true;

		/// <summary>
		/// Gets or sets whether only known types should be allowed.
		/// Default: false
		/// </summary>
		public bool AllowOnlyKnownTypes { get; set; } = false;

		/// <summary>
		/// Gets the collection of allowed type names.
		/// </summary>
		public IReadOnlyCollection<string> AllowedTypes => _allowedTypes;

		/// <summary>
		/// Gets the collection of allowed namespace names.
		/// </summary>
		public IReadOnlyCollection<string> AllowedNamespaces => _allowedNamespaces;

		/// <summary>
		/// Gets the collection of blocked type names.
		/// </summary>
		public IReadOnlyCollection<string> BlockedTypes => _blockedTypes;

		/// <summary>
		/// Gets the collection of blocked namespace names.
		/// </summary>
		public IReadOnlyCollection<string> BlockedNamespaces => _blockedNamespaces;

		/// <summary>
		/// Adds an allowed type name.
		/// </summary>
		/// <param name="typeName">Full type name</param>
		/// <returns>Configuration instance for method chaining</returns>
		public HyperionSerializerConfig AddAllowedType(string typeName)
		{
			if (!string.IsNullOrEmpty(typeName))
				_allowedTypes.Add(typeName);
			return this;
		}

		/// <summary>
		/// Adds an allowed namespace name.
		/// </summary>
		/// <param name="namespaceName">Namespace name</param>
		/// <returns>Configuration instance for method chaining</returns>
		public HyperionSerializerConfig AddAllowedNamespace(string namespaceName)
		{
			if (!string.IsNullOrEmpty(namespaceName))
				_allowedNamespaces.Add(namespaceName);
			return this;
		}

		/// <summary>
		/// Adds a blocked type name.
		/// </summary>
		/// <param name="typeName">Full type name</param>
		/// <returns>Configuration instance for method chaining</returns>
		public HyperionSerializerConfig AddBlockedType(string typeName)
		{
			if (!string.IsNullOrEmpty(typeName))
				_blockedTypes.Add(typeName);
			return this;
		}

		/// <summary>
		/// Adds a blocked namespace name.
		/// </summary>
		/// <param name="namespaceName">Namespace name</param>
		/// <returns>Configuration instance for method chaining</returns>
		public HyperionSerializerConfig AddBlockedNamespace(string namespaceName)
		{
			if (!string.IsNullOrEmpty(namespaceName))
				_blockedNamespaces.Add(namespaceName);
			return this;
		}

		/// <summary>
		/// Checks if a type is allowed for serialization/deserialization.
		/// </summary>
		/// <param name="type">Type to check</param>
		/// <returns>True if type is allowed, false otherwise</returns>
		public bool IsTypeAllowed(Type type)
		{
			if (type == null)
				return false;

			var typeName = type.FullName ?? type.Name;

			// Check blocked types first
			if (_blockedTypes.Contains(typeName))
				return false;

			// Check blocked namespaces
			if (_blockedNamespaces.Any(ns => typeName.StartsWith(ns + ".")))
				return false;

			// If type safety is not enforced, allow by default
			if (!EnforceTypeSafety)
				return true;

			// If only known types are allowed, check allowed lists
			if (AllowOnlyKnownTypes)
			{
				// Check allowed types
				if (_allowedTypes.Contains(typeName))
					return true;

				// Check allowed namespaces
				if (_allowedNamespaces.Any(ns => typeName.StartsWith(ns + ".")))
					return true;

				return false;
			}

			// Default allow if no restrictions are set
			return true;
		}

		/// <summary>
		/// Validates the configuration settings.
		/// </summary>
		/// <exception cref="InvalidOperationException">Thrown when configuration is invalid</exception>
		public void Validate()
		{
			if (MaxSerializedSize <= 0)
				throw new InvalidOperationException("MaxSerializedSize must be greater than 0.");

			if (AllowOnlyKnownTypes && !_allowedTypes.Any() && !_allowedNamespaces.Any())
				throw new InvalidOperationException("When AllowOnlyKnownTypes is enabled, at least one allowed type or namespace must be specified.");
		}

		/// <summary>
		/// Creates a default configuration with common safe settings.
		/// </summary>
		/// <returns>Default configuration</returns>
		public static HyperionSerializerConfig Default()
		{
			return new HyperionSerializerConfig
			{
				PreserveObjectReferences = true,
				IgnoreISerializable = false,
				VersionTolerance = true,
				EnableCompression = false,
				EnforceTypeSafety = true,
				AllowOnlyKnownTypes = false
			};
		}

		/// <summary>
		/// Creates a secure configuration with strict type safety.
		/// </summary>
		/// <returns>Secure configuration</returns>
		public static HyperionSerializerConfig Secure()
		{
			return new HyperionSerializerConfig
			{
				PreserveObjectReferences = true,
				IgnoreISerializable = false,
				VersionTolerance = true,
				EnableCompression = false,
				EnforceTypeSafety = true,
				AllowOnlyKnownTypes = true,
				MaxSerializedSize = 10 * 1024 * 1024 // 10 MB
			};
		}
	}
}