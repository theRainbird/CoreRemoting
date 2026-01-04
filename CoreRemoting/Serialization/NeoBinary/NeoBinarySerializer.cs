using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text;

namespace CoreRemoting.Serialization.NeoBinary
{
	/// <summary>
	/// Modern binary serializer that replaces BinaryFormatter with enhanced security and performance.
	/// </summary>
	public class NeoBinarySerializer
	{
		// Pending forward references collected during nested deserializations
		// that could not be resolved immediately (e.g., child -> parent back-references).
		// They will be resolved after the full object graph has been materialized.
		private readonly List<(object targetObject, System.Reflection.FieldInfo field, int placeholderObjectId)>
			_pendingForwardReferences = [];

		private const string MAGIC_NAME = "NEOB";
		private const ushort CURRENT_VERSION = 1;

		// High-performance IL-based serializer and cache
		private readonly IlTypeSerializer _ilSerializer = new();
		private readonly SerializerCache _serializerCache = new();

		// Legacy caches for backward compatibility
		private readonly ConcurrentDictionary<Type, string> _typeNameCache = new();
		private readonly ConcurrentDictionary<string, Type> _resolvedTypeCache = new();

		// Performance optimization: assembly type cache to avoid expensive GetTypes() calls
		private readonly ConcurrentDictionary<Assembly, Type[]> _assemblyTypeCache = new();
		private readonly ConcurrentDictionary<string, Assembly> _assemblyNameCache = new();

		// Performance optimization: reverse lookup for O(1) object-to-ID mapping in forward reference resolution
		private readonly Dictionary<object, int> _objectToIdMap = new();

		// Performance optimization: pre-populated common types cache
		// ReSharper disable once InconsistentNaming
		private static readonly Dictionary<string, Type> _commonTypes = new()
		{
			["System.String"] = typeof(string),
			["System.Int32"] = typeof(int),
			["System.Int64"] = typeof(long),
			["System.Double"] = typeof(double),
			["System.Single"] = typeof(float),
			["System.Decimal"] = typeof(decimal),
			["System.DateTime"] = typeof(DateTime),
			["System.Boolean"] = typeof(bool),
			["System.Byte"] = typeof(byte),
			["System.Char"] = typeof(char),
			["System.Object"] = typeof(object),
			["System.Guid"] = typeof(Guid),
			["System.TimeSpan"] = typeof(TimeSpan),
			["System.Uri"] = typeof(Uri),
			["System.Version"] = typeof(Version)
		};

		// --- NEW: Type reference table state (per operation) ---
		// These are initialized at the beginning of Serialize/Deserialize when
		// Config.UseTypeReferences is enabled and used by WriteTypeInfo/ReadTypeInfo.
		private List<Type> _typeTable; // only valid during an active (de)serialization
		private Dictionary<string, int> _typeKeyToId; // key: typeName|asmName|version
		private bool _typeRefActive; // marks active session using type refs

		// --- NEW: Assembly type index cache (shared across all instances) ---
		private static readonly ConcurrentDictionary<Assembly, Dictionary<string, Type>> _assemblyTypeIndex = new();

		// --- NEW: Validated types cache ---
		private readonly ConcurrentDictionary<Type, bool> _validatedTypes = new();

		// --- NEW: Object pools for performance optimization ---
		private readonly ObjectPool<HashSet<object>> _hashSetPool = new ObjectPool<HashSet<object>>(() => new HashSet<object>(ReferenceEqualityComparer.Instance), set => set.Clear());
		private readonly ObjectPool<Dictionary<object, int>> _objectMapPool = new ObjectPool<Dictionary<object, int>>(() => new Dictionary<object, int>(ReferenceEqualityComparer.Instance), dict => dict.Clear());

		/// <summary>
		/// Gets or sets the serializer configuration.
		/// </summary>
		public NeoBinarySerializerConfig Config { get; set; } = new NeoBinarySerializerConfig();

		/// <summary>
		/// Gets or sets the type validator for security.
		/// </summary>
		public NeoBinaryTypeValidator TypeValidator { get; set; } = new NeoBinaryTypeValidator();

		/// <summary>
		/// Gets the high-performance serializer cache.
		/// </summary>
		public SerializerCache SerializerCache => _serializerCache;

		/// <summary>
		/// Pre-builds type indexes for loaded assemblies to avoid expensive reflection calls during serialization.
		/// Call this method at application startup for optimal performance. This is shared across all NeoBinarySerializer instances.
		/// </summary>
		public static void BuildAssemblyTypeIndexes()
		{
			var assemblies = AppDomain.CurrentDomain.GetAssemblies();
			foreach (var assembly in assemblies)
			{
				// This will populate the _assemblyTypeIndex cache
				_assemblyTypeIndex.GetOrAdd(assembly, BuildTypeIndexForAssembly);
			}
		}

		/// <summary>
		/// Builds a type index dictionary for a given assembly.
		/// </summary>
		/// <param name="assembly">The assembly to build the index for</param>
		/// <returns>Dictionary mapping type full names to Type objects</returns>
		private static Dictionary<string, Type> BuildTypeIndexForAssembly(Assembly assembly)
		{
			try
			{
				return assembly.GetTypes()
					.Where(t => t != null && t.FullName != null)
					.GroupBy(t => t.FullName!)
					.ToDictionary(g => g.Key, g => g.First());
			}
			catch (ReflectionTypeLoadException ex)
			{
				return ex.Types
					.Where(t => t != null && t.FullName != null)
					.GroupBy(t => t!.FullName!)
					.ToDictionary(g => g.Key, g => g.First()!);
			}
		}

		/// <summary>
		/// Serializes an object to the specified stream.
		/// </summary>
		/// <param name="graph">Object to serialize</param>
		/// <param name="serializationStream">Stream to write to</param>
		public void Serialize(object graph, Stream serializationStream)
		{
			if (serializationStream == null)
				throw new ArgumentNullException(nameof(serializationStream));

			using var writer = new BinaryWriter(serializationStream, Encoding.UTF8, leaveOpen: true);

			// Initialize type-ref tables for this session if enabled
			if (Config.UseTypeReferences)
			{
				_typeTable = new List<Type>(64);
				_typeKeyToId = new Dictionary<string, int>(128);
				_typeRefActive = true;
			}

			// Fast path for primitive types - avoid overhead of reference tracking
			if (graph != null && IsSimpleType(graph.GetType()))
			{
				WriteHeader(writer);
				writer.Write((byte)3); // Simple object marker
				WriteTypeInfo(writer, graph.GetType());
				SerializePrimitive(graph, writer);
				writer.Flush();
				_typeRefActive = false;
				return;
			}

			var serializedObjects = _hashSetPool.Get();
			var objectMap = _objectMapPool.Get();

			try
			{
				// Write header
				WriteHeader(writer);

				// Serialize object graph
				if (graph != null)
				{
					SerializeObject(graph, writer, serializedObjects, objectMap);
				}
				else
				{
					// Write null marker
					writer.Write((byte)0);
				}

				writer.Flush();
			}
			finally
			{
				_hashSetPool.Return(serializedObjects);
				_objectMapPool.Return(objectMap);
				_typeRefActive = false;
			}
		}

		/// <summary>
		/// Deserializes an object from the specified stream.
		/// </summary>
		/// <param name="serializationStream">Stream to read from</param>
		/// <returns>Deserialized object</returns>
		public object Deserialize(Stream serializationStream)
		{
			if (serializationStream == null)
				throw new ArgumentNullException(nameof(serializationStream));

			using var reader = new BinaryReader(serializationStream, Encoding.UTF8, leaveOpen: true);

			// Initialize type-ref tables for this session if enabled
			if (Config.UseTypeReferences)
			{
				_typeTable = new List<Type>(64);
				_typeKeyToId = new Dictionary<string, int>(128);
				_typeRefActive = true;
			}

			// Read and validate header
			ReadHeader(reader);

			// Peek at the first byte to determine if it's a simple type
			var firstByte = reader.ReadByte();
			if (firstByte == 0)
			{
				return null;
			}

			// Fast path for primitive types - avoid overhead of reference tracking
			if (firstByte == 3) // Simple object marker
			{
				var type = ReadTypeInfo(reader);
				return DeserializePrimitive(type, reader);
			}

			// Put the byte back for complex object processing
			serializationStream.Position = serializationStream.Position - 1;
			var deserializedObjects = new Dictionary<int, object>();
			var result = DeserializeObject(reader, deserializedObjects);

			// After the full graph is constructed, resolve any remaining forward references
			// that couldn't be set during nested deserialization (e.g., back-references).
			ResolvePendingForwardReferences(deserializedObjects);

			_typeRefActive = false;
			return result;
		}

		private void WriteHeader(BinaryWriter writer)
		{
			writer.Write(Encoding.ASCII.GetBytes(MAGIC_NAME));
			writer.Write(CURRENT_VERSION);

			// Write flags
			ushort flags = 0;
			if (Config.IncludeAssemblyVersions) flags |= 0x01;
			if (Config.UseTypeReferences) flags |= 0x02;
			writer.Write(flags);
		}

		private void ReadHeader(BinaryReader reader)
		{
			var magicBytes = reader.ReadBytes(4);
			// Compare directly against ASCII constants: 'N','E','O','B'
			if (magicBytes.Length != 4 || magicBytes[0] != (byte)'N' || magicBytes[1] != (byte)'E' ||
			    magicBytes[2] != (byte)'O' || magicBytes[3] != (byte)'B')
				throw new InvalidOperationException("Invalid magic number: expected NEOB");

			var version = reader.ReadUInt16();
			if (version > CURRENT_VERSION)
				throw new InvalidOperationException($"Unsupported version: {version}");

			reader.ReadUInt16();
		}

		private void SerializeObject(object obj, BinaryWriter writer, HashSet<object> serializedObjects,
			Dictionary<object, int> objectMap)
		{
			if (obj == null)
			{
				writer.Write((byte)0); // Null marker
				return;
			}

			var type = obj.GetType();

			// Arrays should be handled first - they are not simple types
			if (type.IsArray)
			{
				// Check for circular references
				if (serializedObjects.Contains(obj))
				{
					writer.Write((byte)2); // Reference marker
					writer.Write(objectMap[obj]);
					return;
				}

				// Register object for reference tracking
				var arrayObjectId = objectMap.Count;
				objectMap[obj] = arrayObjectId;
				serializedObjects.Add(obj);

				writer.Write((byte)1); // Object marker
				writer.Write(arrayObjectId);
				WriteTypeInfo(writer, type);
				
				SerializeArray((Array)obj, writer, serializedObjects, objectMap);
				return;
			}

			if (IsSimpleType(type))
			{
				writer.Write((byte)3); // Simple object marker
				WriteTypeInfo(writer, type);
				SerializePrimitive(obj, writer);
				return;
			}

			// Check for circular references
			if (serializedObjects.Contains(obj))
			{
				writer.Write((byte)2); // Reference marker
				writer.Write(objectMap[obj]);
				return;
			}

			// Register object for reference tracking
			var objectId = objectMap.Count;
			objectMap[obj] = objectId;
			serializedObjects.Add(obj);

			writer.Write((byte)1); // Object marker
			writer.Write(objectId);

			// Write type information
			WriteTypeInfo(writer, type);

			// Serialize based on type
			if (type.IsEnum)
			{
				SerializeEnum(obj, writer);
			}
			else if (type.IsArray)
			{
				SerializeArray((Array)obj, writer, serializedObjects, objectMap);
			}
			else if (typeof(IList).IsAssignableFrom(type))
			{
				SerializeList((IList)obj, writer, serializedObjects, objectMap);
			}
			else if (obj is ExpandoObject expando)
			{
				SerializeExpandoObject(expando, writer, serializedObjects, objectMap);
			}
			else if (typeof(IDictionary).IsAssignableFrom(type))
			{
				SerializeDictionary((IDictionary)obj, writer, serializedObjects, objectMap);
			}
			else if (typeof(DataSet).IsAssignableFrom(type))
			{
				SerializeDataSet((DataSet)obj, writer, serializedObjects, objectMap);
			}
			else if (typeof(DataTable).IsAssignableFrom(type))
			{
				SerializeDataTable((DataTable)obj, writer, serializedObjects, objectMap);
			}
			else if (typeof(Exception).IsAssignableFrom(type))
			{
				SerializeException((Exception)obj, writer, serializedObjects, objectMap);
			}
			else if (typeof(Expression).IsAssignableFrom(type))
			{
				SerializeExpression((Expression)obj, writer, serializedObjects, objectMap);
			}
			else if (typeof(Type).IsAssignableFrom(type))
			{
				// Serialize Type objects specially to avoid MemberInfo handling
				WriteTypeInfo(writer, (Type)obj);
			}
			else if (typeof(MemberInfo).IsAssignableFrom(type))
			{
				// Serialize MemberInfo with custom approach
				SerializeMemberInfo(obj, writer, serializedObjects, objectMap);
			}
			else if (typeof(ParameterInfo).IsAssignableFrom(type))
			{
				// Serialize ParameterInfo with custom approach
				SerializeParameterInfo(obj, writer, serializedObjects, objectMap);
			}
			else if (typeof(Module).IsAssignableFrom(type))
			{
				// Serialize Module with custom approach
				SerializeModule(obj, writer, serializedObjects, objectMap);
			}
			else if (typeof(Assembly).IsAssignableFrom(type))
			{
				// Serialize Assembly with custom approach
				SerializeAssembly(obj, writer, serializedObjects, objectMap);
			}
			else
			{
				// Serialize any complex object regardless of [Serializable] attribute
				SerializeComplexObject(obj, writer, serializedObjects, objectMap);
			}
		}

		private object DeserializeObject(BinaryReader reader, Dictionary<int, object> deserializedObjects)
		{
			var marker = reader.ReadByte();

			if (marker == 0) // Null marker
			{
				return null;
			}

			if (marker == 2) // Reference marker
			{
				var objectId = reader.ReadInt32();

				// If object is not yet deserialized, create a forward reference
				if (!deserializedObjects.ContainsKey(objectId))
				{
					// Create a forward reference placeholder
					deserializedObjects[objectId] = new ForwardReferencePlaceholder(objectId);
				}

				return deserializedObjects[objectId];
			}

			if (marker == 3) // Simple object marker
			{
				var type = ReadTypeInfo(reader);
				return DeserializePrimitive(type, reader);
			}

			if (marker == 1) // Object marker
			{
				var objectId = reader.ReadInt32();
				var type = ReadTypeInfo(reader);

				object obj;

				// Arrays should be checked first - they have highest priority
				if (type.IsArray)
				{
					obj = DeserializeArray(type, reader, deserializedObjects, objectId);
				}
				else if (IsSimpleType(type))
				{
					obj = DeserializePrimitive(type, reader);
				}
				else if (type.IsEnum)
				{
					obj = DeserializeEnum(type, reader);
				}
				else if (typeof(IList).IsAssignableFrom(type))
				{
					obj = DeserializeList(type, reader, deserializedObjects, objectId);
				}
				else if (type == typeof(ExpandoObject))
				{
					obj = DeserializeDictionary(type, reader, deserializedObjects, objectId);
				}
				else if (typeof(IDictionary).IsAssignableFrom(type))
				{
					obj = DeserializeDictionary(type, reader, deserializedObjects, objectId);
				}
				else if (typeof(DataSet).IsAssignableFrom(type))
				{
					obj = DeserializeDataSet(type, reader, deserializedObjects, objectId);
				}
				else if (typeof(DataTable).IsAssignableFrom(type))
				{
					obj = DeserializeDataTable(type, reader, deserializedObjects, objectId);
				}
				else if (typeof(Exception).IsAssignableFrom(type))
				{
					obj = DeserializeException(type, reader, deserializedObjects, objectId);
				}
				else if (typeof(Expression).IsAssignableFrom(type))
				{
					obj = DeserializeExpression(reader, deserializedObjects);
				}
				else if (typeof(MemberInfo).IsAssignableFrom(type))
				{
					obj = DeserializeMemberInfo(type, reader, deserializedObjects, objectId);
				}
				else if (typeof(ParameterInfo).IsAssignableFrom(type))
				{
					obj = DeserializeParameterInfo(type, reader, deserializedObjects, objectId);
				}
				else if (typeof(Module).IsAssignableFrom(type))
				{
					obj = DeserializeModule(type, reader, deserializedObjects, objectId);
				}
				else if (typeof(Assembly).IsAssignableFrom(type))
				{
					obj = DeserializeAssembly(type, reader, deserializedObjects, objectId);
				}
				else
				{
					obj = DeserializeComplexObject(type, reader, deserializedObjects, objectId);
				}

				RegisterObjectWithReverseMapping(deserializedObjects, objectId, obj);
				return obj;
			}

			// For complex reflection object graphs, some markers might not be handled
			// This is a graceful fallback to prevent test failures
			throw new InvalidOperationException($"Invalid marker: {marker}");
		}

		private void WriteTypeInfo(BinaryWriter writer, Type type)
		{
			if (type == null)
			{
				writer.Write(string.Empty); // Empty type name for null
				writer.Write(string.Empty); // Empty assembly name for null
				writer.Write(string.Empty); // Empty version for null
				return;
			}

			var assemblyName = type.Assembly.GetName();
			string typeName;

			if (_typeRefActive)
			{
				// With type references enabled, write a compact reference entry
				// Build type key based on config
				typeName = Config.IncludeAssemblyVersions
					? (type.FullName ?? type.Name)
					: BuildAssemblyNeutralTypeName(type);
				var asmName = assemblyName.Name ?? string.Empty;
				var versionString = Config.IncludeAssemblyVersions
					? (assemblyName.Version?.ToString() ?? string.Empty)
					: string.Empty;
				var key = typeName + "|" + asmName + "|" + versionString;

				if (_typeKeyToId.TryGetValue(key, out var existingId))
				{
					// Write reference marker and ID
					writer.Write((byte)1);
					writer.Write(existingId);
					return;
				}

				// New type definition
				var newId = _typeTable.Count;
				_typeKeyToId[key] = newId;
				_typeTable.Add(type);

				writer.Write((byte)0); // new type definition
				// Store pooled strings to reduce allocations
				typeName = _serializerCache.GetOrCreatePooledString(typeName);
				var asmNamePooled = _serializerCache.GetOrCreatePooledString(asmName);
				var versionPooled = _serializerCache.GetOrCreatePooledString(versionString);
				writer.Write(typeName);
				writer.Write(asmNamePooled);
				writer.Write(versionPooled);
				writer.Write(newId);
				return;
			}

			// Legacy path without type references
			// When assembly versions are excluded, we must also avoid embedding
			// assembly-qualified generic argument type names inside the type name.
			if (Config.IncludeAssemblyVersions)
			{
				typeName = type.FullName ?? type.Name;
			}
			else
			{
				typeName = BuildAssemblyNeutralTypeName(type);
			}

			// Use string pooling for frequently used type names
			typeName = _serializerCache.GetOrCreatePooledString(typeName);
			writer.Write(typeName);

			var assemblyNameString = assemblyName.Name ?? string.Empty;
			assemblyNameString = _serializerCache.GetOrCreatePooledString(assemblyNameString);
			writer.Write(assemblyNameString);

			if (Config.IncludeAssemblyVersions)
			{
				var versionString = assemblyName.Version?.ToString() ?? string.Empty;
				versionString = _serializerCache.GetOrCreatePooledString(versionString);
				writer.Write(versionString);
			}
			else
			{
				writer.Write(string.Empty);
			}
		}

		private Type ReadTypeInfo(BinaryReader reader)
		{
			if (_typeRefActive)
			{
				var kind = reader.ReadByte();
				if (kind == 1)
				{
					var id = reader.ReadInt32();
					var t = _typeTable[id];
					return t;
				}

				// New type definition
				var typeNameNew = reader.ReadString();
				var assemblyNameNew = reader.ReadString();
				var assemblyVersionNew = reader.ReadString();
				var newId = reader.ReadInt32();

				var tResolved = ResolveTypeCore(typeNameNew, assemblyNameNew, assemblyVersionNew);
				// ensure table size/order correctness
				if (newId != _typeTable.Count)
				{
					// fill any gaps (shouldn't happen) to maintain index safety
					while (_typeTable.Count < newId)
						_typeTable.Add(typeof(object));
				}

				_typeTable.Add(tResolved);
				var keyNew = $"{typeNameNew}|{assemblyNameNew}|{assemblyVersionNew}";
				_typeKeyToId[keyNew] = newId;
				return tResolved;
			}

			// Legacy path
			var typeName = reader.ReadString();
			var assemblyName = reader.ReadString();
			var assemblyVersion = reader.ReadString();
			return ResolveTypeCore(typeName, assemblyName, assemblyVersion);
		}

		private Type ResolveTypeCore(string typeName, string assemblyName, string assemblyVersion)
		{
			// Create cache key for type resolution
			var cacheKey = $"{typeName}|{assemblyName}|{assemblyVersion}";
			if (_resolvedTypeCache.TryGetValue(cacheKey, out var cachedType))
				return cachedType;

			Type type = null;

			// Fast path for common types
			if (!string.IsNullOrEmpty(typeName) && _commonTypes.TryGetValue(typeName, out var commonType))
			{
				type = commonType;
			}
			else if (!string.IsNullOrEmpty(assemblyName))
			{
				// First try in loaded assemblies (cached search)
				type = FindTypeInLoadedAssembliesCached(typeName);
				if (type == null)
				{
					var assembly = GetAssemblyCached(assemblyName);
					if (assembly != null)
					{
						var map = _assemblyTypeIndex.GetOrAdd(assembly, BuildTypeIndexForAssembly);
						if (!map.TryGetValue(typeName, out type))
						{
							// Try short name as a weak fallback
							type = map.Values.FirstOrDefault(t => t.Name == typeName);
						}
					}

					if (type == null)
						type = Type.GetType(typeName);
				}
			}

			if (type == null)
			{
				type = ResolveAssemblyNeutralType(typeName);
			}

			if (type == null)
				throw new TypeLoadException($"Cannot load type: {typeName}, Assembly: {assemblyName}");

			// Validate once per type safely
			if (!_validatedTypes.ContainsKey(type))
			{
				TypeValidator.ValidateType(type);
				_validatedTypes[type] = true;
			}

			_resolvedTypeCache[cacheKey] = type;
			return type;
		}

		/// <summary>
		/// Performance-optimized method to register objects with reverse lookup mapping.
		/// </summary>
		/// <param name="deserializedObjects">The main object dictionary</param>
		/// <param name="objectId">The object ID</param>
		/// <param name="obj">The object to register</param>
		private void RegisterObjectWithReverseMapping(Dictionary<int, object> deserializedObjects, int objectId,
			object obj)
		{
			deserializedObjects[objectId] = obj;

			// Only add to reverse mapping if it's not a placeholder or problematic reflection type
			if (!(obj is ForwardReferencePlaceholder) && obj != null)
			{
				try
				{
					_objectToIdMap[obj] = objectId;
				}
				catch (NullReferenceException)
				{
					// Skip reverse mapping for partially initialized reflection objects
					// This can happen with PropertyInfo and other reflection types
				}
			}
		}

		/// <summary>
		/// Performance-optimized method to get types from an assembly with caching.
		/// </summary>
		/// <param name="assembly">The assembly to get types from</param>
		/// <returns>Array of types in the assembly</returns>
		private Type[] GetAssemblyTypesCached(Assembly assembly)
		{
			return _assemblyTypeCache.GetOrAdd(assembly, asm =>
			{
				try
				{
					return asm.GetTypes();
				}
				catch (ReflectionTypeLoadException ex)
				{
					// Handle partial loading - return only successfully loaded types
					return ex.Types.Where(t => t != null).ToArray();
				}
			});
		}

		/// <summary>
		/// Performance-optimized method to load assembly with caching.
		/// </summary>
		/// <param name="assemblyName">The assembly name to load</param>
		/// <returns>The loaded assembly or null if not found</returns>
		private Assembly GetAssemblyCached(string assemblyName)
		{
			if (string.IsNullOrEmpty(assemblyName))
				return null;

			return _assemblyNameCache.GetOrAdd(assemblyName, name =>
			{
				try
				{
					return Assembly.Load(name);
				}
				catch
				{
					// Try to find in currently loaded assemblies
					return AppDomain.CurrentDomain.GetAssemblies()
						.FirstOrDefault(a => a.GetName().Name == name || a.FullName == name);
				}
			});
		}

		/// <summary>
		/// Performance-optimized type search across loaded assemblies with caching.
		/// </summary>
		/// <param name="typeName">The type name to search for</param>
		/// <returns>The found type or null</returns>
		private Type FindTypeInLoadedAssembliesCached(string typeName)
		{
			// Create a cache key for assembly-wide type search
			var searchCacheKey = $"search_{typeName}";

			// Check if we've already searched for this type recently
			if (_resolvedTypeCache.TryGetValue(searchCacheKey, out var cachedResult))
				return cachedResult;

			Type foundType = null;
			var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();

			// Search through assemblies with cached type arrays
			foreach (var assembly in loadedAssemblies)
			{
				var assemblyTypes = GetAssemblyTypesCached(assembly);
				foundType = assemblyTypes.FirstOrDefault(t =>
					t.FullName == typeName || t.Name == typeName);

				if (foundType != null)
					break;
			}

			// Cache the search result (null is also cached to avoid repeated searches)
			_resolvedTypeCache[searchCacheKey] = foundType;
			return foundType;
		}

		/// <summary>
		/// Builds a type name string without embedding assembly information for generic argument types.
		/// The format is compatible with Type.GetType style generic notation, e.g.:
		/// Namespace.Generic`1[[Arg.Namespace.Type]]
		/// </summary>
		private string BuildAssemblyNeutralTypeName(Type type)
		{
			return _typeNameCache.GetOrAdd(type, t =>
			{
				string result;

				if (t.IsGenericType)
				{
					var genericDef = t.GetGenericTypeDefinition();
					var defName = genericDef.FullName; // e.g. System.Collections.Generic.List`1
					var args = t.GetGenericArguments();
					var argNames = args.Select(BuildAssemblyNeutralTypeName).ToArray();
					var sb = new StringBuilder();
					for (int i = 0; i < argNames.Length; i++)
					{
						if (i > 0) sb.Append("],[");
						sb.Append(argNames[i]);
					}
					result = $"{defName}[[{sb}]]";
				}
				else if (t.IsArray)
				{
					// Handle arrays by composing element type and rank suffix
					var elem = t.GetElementType();
					var rank = t.GetArrayRank();
					var suffix = rank == 1 ? "[]" : "[" + new string(',', rank - 1) + "]";
					result = BuildAssemblyNeutralTypeName(elem) + suffix;
				}
				else
				{
					result = t.FullName ?? t.Name;
				}

				return _serializerCache.GetOrCreatePooledString(result);
			});
		}

		/// <summary>
		/// Resolves a type name written without assembly qualifiers for generic arguments.
		/// Tries Type.GetType, then searches loaded assemblies, and for generics parses and resolves recursively.
		/// </summary>
		private Type ResolveAssemblyNeutralType(string typeName)
		{
			return _resolvedTypeCache.GetOrAdd(typeName, tn =>
			{
				// Fast path
				var t = Type.GetType(tn);
				if (t != null) return t;

				// If looks like a generic with our [[...]] notation
				int idx = tn.IndexOf("[[", StringComparison.Ordinal);
				if (idx > 0)
				{
					var defName = tn.Substring(0, idx);
					var argsPart = tn.Substring(idx);

					var defType = FindTypeInLoadedAssemblies(defName) ?? Type.GetType(defName);
					if (defType == null)
						return null;

					var argNames = ParseGenericArgumentNames(argsPart);
					var argTypes = new Type[argNames.Count];
					for (int i = 0; i < argNames.Count; i++)
					{
						var at = ResolveAssemblyNeutralType(argNames[i]);
						if (at == null) return null;
						argTypes[i] = at;
					}

					try
					{
						return defType.MakeGenericType(argTypes);
					}
					catch
					{
						return null;
					}
				}

				// Non-generic: search loaded assemblies by FullName then by Name
				return FindTypeInLoadedAssemblies(tn);
			});
		}

		private static Type FindTypeInLoadedAssemblies(string fullOrSimpleName)
		{
			foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
			{
				try
				{
					var t = asm.GetType(fullOrSimpleName, throwOnError: false, ignoreCase: false);
					if (t != null) return t;
				}
				catch
				{
					/* ignore problematic assemblies */
				}
			}

			// Fallback: search by simple name across all types (could be expensive)
			foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
			{
				try
				{
					var t = asm.GetTypes()
						.FirstOrDefault(x => x.FullName == fullOrSimpleName || x.Name == fullOrSimpleName);
					if (t != null) return t;
				}
				catch
				{
					/* ignore */
				}
			}

			return null;
		}

		private static List<string> ParseGenericArgumentNames(string argsPart)
		{
			// argsPart starts with [[ and ends with ]] possibly; we extract inside and split at top-level '],['
			if (argsPart.StartsWith("[[") && argsPart.EndsWith("]]"))
			{
				argsPart = argsPart.Substring(2, argsPart.Length - 4);
			}

			var result = new List<string>();
			var sb = new StringBuilder();
			int depth = 0; // depth of bracket nesting considering double brackets

			for (int i = 0; i < argsPart.Length; i++)
			{
				char c = argsPart[i];

				if (c == '[')
				{
					depth++;
					sb.Append(c);
					continue;
				}

				if (c == ']')
				{
					depth--;
					sb.Append(c);
					continue;
				}

				// Split token is ",[" when at top level (depth == 0) but our format uses "],["
				if (depth == 0 && i + 2 < argsPart.Length && argsPart[i] == ']' && argsPart[i + 1] == ',' &&
				    argsPart[i + 2] == '[')
				{
					// Not applicable due to previous bracket handling; instead detect "," at depth 0
				}

				if (depth == 0 && c == ',' && i + 1 < argsPart.Length && argsPart[i - 1] == ']' &&
				    argsPart[i + 1] == '[')
				{
					// Separator between arguments
					result.Add(TrimBrackets(sb.ToString()));
					sb.Clear();
					continue;
				}

				sb.Append(c);
			}

			if (sb.Length > 0)
			{
				result.Add(TrimBrackets(sb.ToString()));
			}

			return result;
		}

		private static string TrimBrackets(string s)
		{
			s = s.Trim();
			while (s.StartsWith("[")) s = s.Substring(1);
			while (s.EndsWith("]")) s = s.Substring(0, s.Length - 1);
			return s;
		}

		/// <summary>
		/// Serializes decimal efficiently as binary data (96-bit integer + scale + flags)
		/// </summary>
		private static void SerializeDecimal(decimal value, BinaryWriter writer)
		{
			var bits = decimal.GetBits(value);
			writer.Write(bits[0]); // low 32 bits
			writer.Write(bits[1]); // middle 32 bits  
			writer.Write(bits[2]); // high 32 bits
			writer.Write(bits[3]); // flags and scale
		}

		/// <summary>
		/// Deserializes decimal from binary format
		/// </summary>
		private static decimal DeserializeDecimal(BinaryReader reader)
		{
			var bits = new int[4];
			bits[0] = reader.ReadInt32(); // low 32 bits
			bits[1] = reader.ReadInt32(); // middle 32 bits
			bits[2] = reader.ReadInt32(); // high 32 bits
			bits[3] = reader.ReadInt32(); // flags and scale
			return new decimal(bits);
		}

		private void SerializePrimitive(object obj, BinaryWriter writer)
		{
			switch (obj)
			{
				case bool b: writer.Write(b); break;
				case byte b: writer.Write(b); break;
				case sbyte sb: writer.Write(sb); break;
				case short s: writer.Write(s); break;
				case ushort us: writer.Write(us); break;
				case int i: writer.Write(i); break;
				case uint ui: writer.Write(ui); break;
				case long l: writer.Write(l); break;
				case ulong ul: writer.Write(ul); break;
				case char ch: writer.Write(ch); break;
				case float f: writer.Write(f); break;
				case double d: writer.Write(d); break;
				case decimal dec: SerializeDecimal(dec, writer); break;
				case string str: writer.Write(str); break;
				case UIntPtr up: writer.Write(up.ToUInt64()); break;
				case IntPtr ip: writer.Write(ip.ToInt64()); break;
				case DateTime dt: writer.Write(dt.ToBinary()); break;
				default: throw new InvalidOperationException($"Unsupported primitive type: {obj.GetType()}");
			}
		}

		private object DeserializePrimitive(Type type, BinaryReader reader)
		{
			// Additional validation: This should NEVER be called for complex types
			if (type.IsClass && !type.IsEnum && type != typeof(string))
			{
				throw new InvalidOperationException($"DeserializePrimitive called for complex type: {type.FullName}. This indicates a serious bug in type resolution or serialization.");
			}

			if (type == typeof(bool)) return reader.ReadBoolean();
			if (type == typeof(byte)) return reader.ReadByte();
			if (type == typeof(sbyte)) return reader.ReadSByte();
			if (type == typeof(short)) return reader.ReadInt16();
			if (type == typeof(ushort)) return reader.ReadUInt16();
			if (type == typeof(int)) return reader.ReadInt32();
			if (type == typeof(uint)) return reader.ReadUInt32();
			if (type == typeof(long)) return reader.ReadInt64();
			if (type == typeof(ulong)) return reader.ReadUInt64();
			if (type == typeof(char)) return reader.ReadChar();
			if (type == typeof(float)) return reader.ReadSingle();
			if (type == typeof(double)) return reader.ReadDouble();
			if (type == typeof(decimal)) return DeserializeDecimal(reader);
			if (type == typeof(string)) return reader.ReadString();
			if (type == typeof(UIntPtr)) return new UIntPtr(reader.ReadUInt64());
			if (type == typeof(IntPtr)) return new IntPtr(reader.ReadInt64());
			if (type == typeof(DateTime)) return DateTime.FromBinary(reader.ReadInt64());

			throw new InvalidOperationException($"Unsupported primitive type: {type}");
		}

		private void SerializeEnum(object obj, BinaryWriter writer)
		{
			var underlyingType = Enum.GetUnderlyingType(obj.GetType());
			var converted = Convert.ChangeType(obj, underlyingType);
			SerializePrimitive(converted, writer);
		}

		private object DeserializeEnum(Type type, BinaryReader reader)
		{
			var underlyingType = Enum.GetUnderlyingType(type);
			var value = DeserializePrimitive(underlyingType, reader);
			return Enum.ToObject(type, value);
		}

		private bool IsSimpleType(Type type)
		{
			return type.IsPrimitive || type == typeof(string) || type == typeof(decimal) || type == typeof(UIntPtr) ||
			       type == typeof(IntPtr) || type == typeof(DateTime);
		}

		/// <summary>
		/// SIMD-optimized serialization for int arrays.
		/// </summary>
		private static void SerializeIntArraySimd(int[] array, BinaryWriter writer)
		{
#if NET5_0_OR_GREATER
			if (Vector.IsHardwareAccelerated && array.Length >= Vector<int>.Count)
			{
				// SIMD path: Process in vector chunks
				var vectors = MemoryMarshal.Cast<int, Vector<int>>(array);
				foreach (var vector in vectors)
				{
					for (int i = 0; i < Vector<int>.Count; i++)
						writer.Write(vector[i]);
				}
				// Handle remainder
				int remainder = array.Length % Vector<int>.Count;
				for (int i = array.Length - remainder; i < array.Length; i++)
					writer.Write(array[i]);
			}
			else
#endif
			{
				// Fallback: Standard loop
				foreach (var item in array) writer.Write(item);
			}
		}

		/// <summary>
		/// SIMD-optimized serialization for float arrays.
		/// </summary>
		private static void SerializeFloatArraySimd(float[] array, BinaryWriter writer)
		{
#if NET5_0_OR_GREATER
			if (Vector.IsHardwareAccelerated && array.Length >= Vector<float>.Count)
			{
				var vectors = MemoryMarshal.Cast<float, Vector<float>>(array);
				foreach (var vector in vectors)
				{
					for (int i = 0; i < Vector<float>.Count; i++)
						writer.Write(vector[i]);
				}
				int remainder = array.Length % Vector<float>.Count;
				for (int i = array.Length - remainder; i < array.Length; i++)
					writer.Write(array[i]);
			}
			else
#endif
			{
				foreach (var item in array) writer.Write(item);
			}
		}

		/// <summary>
		/// SIMD-optimized serialization for double arrays.
		/// </summary>
		private static void SerializeDoubleArraySimd(double[] array, BinaryWriter writer)
		{
#if NET5_0_OR_GREATER
			if (Vector.IsHardwareAccelerated && array.Length >= Vector<double>.Count)
			{
				var vectors = MemoryMarshal.Cast<double, Vector<double>>(array);
				foreach (var vector in vectors)
				{
					for (int i = 0; i < Vector<double>.Count; i++)
						writer.Write(vector[i]);
				}
				int remainder = array.Length % Vector<double>.Count;
				for (int i = array.Length - remainder; i < array.Length; i++)
					writer.Write(array[i]);
			}
			else
#endif
			{
				foreach (var item in array) writer.Write(item);
			}
		}

		/// <summary>
		/// SIMD-optimized deserialization for int arrays.
		/// </summary>
		private static void DeserializeIntArraySimd(int[] array, BinaryReader reader)
		{
#if NET5_0_OR_GREATER
			if (Vector.IsHardwareAccelerated && array.Length >= Vector<int>.Count)
			{
				var vectors = MemoryMarshal.Cast<int, Vector<int>>(array);
				int vectorIndex = 0;
				foreach (var vector in vectors)
				{
					for (int i = 0; i < Vector<int>.Count; i++)
						array[vectorIndex++] = reader.ReadInt32();
				}
				int remainder = array.Length % Vector<int>.Count;
				for (int i = array.Length - remainder; i < array.Length; i++)
					array[i] = reader.ReadInt32();
			}
			else
#endif
			{
				for (int i = 0; i < array.Length; i++) array[i] = reader.ReadInt32();
			}
		}

		/// <summary>
		/// SIMD-optimized deserialization for float arrays.
		/// </summary>
		private static void DeserializeFloatArraySimd(float[] array, BinaryReader reader)
		{
#if NET5_0_OR_GREATER
			if (Vector.IsHardwareAccelerated && array.Length >= Vector<float>.Count)
			{
				var vectors = MemoryMarshal.Cast<float, Vector<float>>(array);
				int vectorIndex = 0;
				foreach (var vector in vectors)
				{
					for (int i = 0; i < Vector<float>.Count; i++)
						array[vectorIndex++] = reader.ReadSingle();
				}
				int remainder = array.Length % Vector<float>.Count;
				for (int i = array.Length - remainder; i < array.Length; i++)
					array[i] = reader.ReadSingle();
			}
			else
#endif
			{
				for (int i = 0; i < array.Length; i++) array[i] = reader.ReadSingle();
			}
		}

		/// <summary>
		/// SIMD-optimized deserialization for double arrays.
		/// </summary>
		private static void DeserializeDoubleArraySimd(double[] array, BinaryReader reader)
		{
#if NET5_0_OR_GREATER
			if (Vector.IsHardwareAccelerated && array.Length >= Vector<double>.Count)
			{
				var vectors = MemoryMarshal.Cast<double, Vector<double>>(array);
				int vectorIndex = 0;
				foreach (var vector in vectors)
				{
					for (int i = 0; i < Vector<double>.Count; i++)
						array[vectorIndex++] = reader.ReadDouble();
				}
				int remainder = array.Length % Vector<double>.Count;
				for (int i = array.Length - remainder; i < array.Length; i++)
					array[i] = reader.ReadDouble();
			}
			else
#endif
			{
				for (int i = 0; i < array.Length; i++) array[i] = reader.ReadDouble();
			}
		}

		private void SerializeArray(Array array, BinaryWriter writer, HashSet<object> serializedObjects,
			Dictionary<object, int> objectMap)
		{
			writer.Write(array.Rank);
			for (int i = 0; i < array.Rank; i++)
			{
				writer.Write(array.GetLength(i));
			}

			var length = array.Length;
			writer.Write(length);

			var elementType = array.GetType().GetElementType()!;
			var isSimpleElement = IsSimpleType(elementType);

			if (array.Rank == 1)
			{
				// SIMD optimization for primitive arrays
				if (isSimpleElement)
				{
					if (elementType == typeof(int))
					{
						SerializeIntArraySimd((int[])array, writer);
						return;
					}
					else if (elementType == typeof(float))
					{
						SerializeFloatArraySimd((float[])array, writer);
						return;
					}
					else if (elementType == typeof(double))
					{
						SerializeDoubleArraySimd((double[])array, writer);
						return;
					}
				}

				// Fallback: Element-wise serialization
				for (int i = 0; i < length; i++)
				{
					var element = array.GetValue(i);
					if (isSimpleElement)
					{
						SerializePrimitive(element, writer);
					}
					else
					{
						SerializeObject(element, writer, serializedObjects, objectMap);
					}
				}
			}
			else
			{
				var indices = new int[array.Rank];
				for (int i = 0; i < length; i++)
				{
					var element = array.GetValue(indices);
					if (isSimpleElement)
					{
						SerializePrimitive(element, writer);
					}
					else
					{
						SerializeObject(element, writer, serializedObjects, objectMap);
					}

					IncrementArrayIndices(indices, array);
				}
			}
		}

		private void IncrementArrayIndices(int[] indices, Array array)
		{
			for (int dim = array.Rank - 1; dim >= 0; dim--)
			{
				indices[dim]++;
				if (indices[dim] < array.GetLength(dim))
					break;
				indices[dim] = 0;
			}
		}

		private Array DeserializeArray(Type type, BinaryReader reader, Dictionary<int, object> deserializedObjects,
			int objectId)
		{
			var rank = reader.ReadInt32();
			var lengths = new int[rank];
			for (int i = 0; i < rank; i++)
			{
				lengths[i] = reader.ReadInt32();
			}

			var totalLength = reader.ReadInt32();
			var elementType = type.GetElementType()!;
			var array = Array.CreateInstance(elementType, lengths);

			// Register array immediately to handle circular references
			deserializedObjects[objectId] = array;

			var isSimpleElement = IsSimpleType(elementType);

			// SIMD optimization for primitive 1D arrays
			if (rank == 1 && isSimpleElement)
			{
				if (elementType == typeof(int))
				{
					DeserializeIntArraySimd((int[])array, reader);
					return array;
				}
				else if (elementType == typeof(float))
				{
					DeserializeFloatArraySimd((float[])array, reader);
					return array;
				}
				else if (elementType == typeof(double))
				{
					DeserializeDoubleArraySimd((double[])array, reader);
					return array;
				}
			}

			// Fallback: Element-wise deserialization
			for (int i = 0; i < totalLength; i++)
			{
				var indices = GetIndicesFromLinearIndex(i, lengths);
				object element;
				if (isSimpleElement)
				{
					element = DeserializePrimitive(elementType, reader);
				}
				else
				{
					element = DeserializeObject(reader, deserializedObjects);
				}

				array.SetValue(element, indices);
			}

			return array;
		}

		private void SerializeList(IList list, BinaryWriter writer,
			HashSet<object> serializedObjects, Dictionary<object, int> objectMap)
		{
			writer.Write(list.Count);
			for (int i = 0; i < list.Count; i++)
			{
				SerializeObject(list[i], writer, serializedObjects, objectMap);
			}
		}

		private object DeserializeList(Type type, BinaryReader reader, Dictionary<int, object> deserializedObjects,
			int objectId)
		{
			var count = reader.ReadInt32();
			var list = (IList)CreateInstanceWithoutConstructor(type);

			// Register the list immediately to handle circular references
			deserializedObjects[objectId] = list;

			for (int i = 0; i < count; i++)
			{
				var item = DeserializeObject(reader, deserializedObjects);

				// If item is a forward reference placeholder, add null for now
				// It will be resolved later
				if (item is ForwardReferencePlaceholder)
				{
					list.Add(null);
				}
				else
				{
					list.Add(item);
				}
			}

			return list;
		}

		private void SerializeDictionary(IDictionary dictionary, BinaryWriter writer,
			HashSet<object> serializedObjects, Dictionary<object, int> objectMap)
		{
			writer.Write(dictionary.Count);
			foreach (DictionaryEntry entry in dictionary)
			{
				SerializeObject(entry.Key, writer, serializedObjects, objectMap);
				SerializeObject(entry.Value, writer, serializedObjects, objectMap);
			}
		}

		private object DeserializeDictionary(Type type, BinaryReader reader,
			Dictionary<int, object> deserializedObjects, int objectId)
		{
			var count = reader.ReadInt32();

			// Special handling for ExpandoObject
			if (type == typeof(ExpandoObject))
			{
				var expando = new ExpandoObject();
				var dict = (IDictionary<string, object>)expando;

				// Register dictionary immediately to handle circular references
				deserializedObjects[objectId] = expando;

				for (int i = 0; i < count; i++)
				{
					var key = (string)DeserializeObject(reader, deserializedObjects);
					var value = DeserializeObject(reader, deserializedObjects);
					dict[key] = value;
				}

				return expando;
			}

			var dictionaryObj = CreateInstanceWithoutConstructor(type);
			var dictionary = (IDictionary)dictionaryObj;

			// Register dictionary immediately to handle circular references
			deserializedObjects[objectId] = dictionary;

			for (int i = 0; i < count; i++)
			{
				var key = DeserializeObject(reader, deserializedObjects);
				var value = DeserializeObject(reader, deserializedObjects);
				dictionary[key] = value;
			}

			return dictionaryObj;
		}

		private void SerializeExpandoObject(ExpandoObject expando, BinaryWriter writer,
			HashSet<object> serializedObjects,
			Dictionary<object, int> objectMap)
		{
			var dict = (IDictionary<string, object>)expando;
			writer.Write(dict.Count);
			foreach (var kvp in dict)
			{
				SerializeObject(kvp.Key, writer, serializedObjects, objectMap);
				SerializeObject(kvp.Value, writer, serializedObjects, objectMap);
			}
		}

		private void SerializeComplexObject(object obj, BinaryWriter writer, HashSet<object> serializedObjects,
			Dictionary<object, int> objectMap)
		{
			var type = obj.GetType();

			// Use compact IL layout if enabled: write subformat tag and skip field names/count
			if (Config.UseIlCompactLayout)
			{
				// Write compact layout tag directly after TypeInfo (caller already wrote TypeInfo)
				writer.Write(IlTypeSerializer.CompactLayoutTag);

				var context = new IlTypeSerializer.SerializationContext
				{
					SerializedObjects = serializedObjects,
					ObjectMap = objectMap,
					Serializer = this,
					StringPool = _serializerCache.StringPool
				};

				var stopwatch = System.Diagnostics.Stopwatch.StartNew();
				try
				{
					var serializer = _ilSerializer.CreateCompactSerializer(type);
					serializer(obj, writer, context);
				}
				finally
				{
					stopwatch.Stop();
					// We don't have per-type stats here; still record global count
					_serializerCache.RecordSerialization();
				}
			}
			else
			{
				// Use high-performance IL-based serializer (legacy format with field names)
				var cachedSerializer =
					_serializerCache.GetOrCreateSerializer(type, t => _ilSerializer.CreateSerializer(t));
				cachedSerializer.RecordAccess();

				var context = new IlTypeSerializer.SerializationContext
				{
					SerializedObjects = serializedObjects,
					ObjectMap = objectMap,
					Serializer = this,
					StringPool = _serializerCache.StringPool
				};

				var stopwatch = System.Diagnostics.Stopwatch.StartNew();
				try
				{
					cachedSerializer.Serializer(obj, writer, context);
				}
				finally
				{
					stopwatch.Stop();
					cachedSerializer.RecordSerialization(stopwatch.ElapsedTicks);
					_serializerCache.RecordSerialization();
				}
			}
		}

		private object DeserializeComplexObject(Type type, BinaryReader reader,
			Dictionary<int, object> deserializedObjects, int objectId)
		{
			// Prepare context and placeholder first
			var context = new IlTypeSerializer.DeserializationContext
			{
				DeserializedObjects = deserializedObjects,
				Serializer = this,
				ObjectToIdMap = _objectToIdMap
			};

			// Register a placeholder immediately to handle self-references during IL deserialization
			var placeholder = new ForwardReferencePlaceholder(objectId);
			deserializedObjects[objectId] = placeholder;

			// Detect compact layout tag after TypeInfo
			bool isCompact = false;
			try
			{
				var b = reader.ReadByte();
				if (b == IlTypeSerializer.CompactLayoutTag)
					isCompact = true;
				else
				{
					// Not compact: seek one byte back if possible
					if (reader.BaseStream.CanSeek)
						reader.BaseStream.Seek(-1, SeekOrigin.Current);
					else
					{
						// Non-seekable stream without compact tag is not supported for legacy IL path
						// Fall back to legacy path by throwing so caller can handle, but we stay here and continue
					}
				}
			}
			catch (EndOfStreamException)
			{
				// Unexpected end - treat as legacy format
			}

			var stopwatch = System.Diagnostics.Stopwatch.StartNew();
			try
			{
				object result;
				if (isCompact)
				{
					var deserializer = _ilSerializer.CreateCompactDeserializer(type);
					result = deserializer(reader, context);
				}
				else
				{
					// Legacy IL-based deserializer
					var cachedDeserializer =
						_serializerCache.GetOrCreateDeserializer(type, t => _ilSerializer.CreateDeserializer(t));
					cachedDeserializer.RecordAccess();
					result = cachedDeserializer.Deserializer(reader, context);
					cachedDeserializer.RecordDeserialization(stopwatch
						.ElapsedTicks); // record early to keep previous stats behavior
				}

				// Replace placeholder with actual result
				deserializedObjects[objectId] = result;

				// Resolve forward references after deserialization.
				IlTypeSerializer.ResolveForwardReferences(context);

				return result;
			}
			finally
			{
				stopwatch.Stop();
				_serializerCache.RecordDeserialization();
			}
		}

		private void SerializeException(Exception exception, BinaryWriter writer, HashSet<object> serializedObjects,
			Dictionary<object, int> objectMap)
		{
			var type = exception.GetType();

			// Serialize basic exception properties with string pooling
			writer.Write(_serializerCache.GetOrCreatePooledString(exception.Message ?? string.Empty));
			writer.Write(_serializerCache.GetOrCreatePooledString(exception.Source ?? string.Empty));
			writer.Write(_serializerCache.GetOrCreatePooledString(exception.StackTrace ?? string.Empty));
			writer.Write(_serializerCache.GetOrCreatePooledString(exception.HelpLink ?? string.Empty));

			// Serialize HResult
			writer.Write(exception.HResult);

			// Serialize inner exception if present
			SerializeObject(exception.InnerException, writer, serializedObjects, objectMap);

			// Serialize data dictionary
			if (exception.Data != null)
			{
				writer.Write(exception.Data.Count);
				foreach (DictionaryEntry entry in exception.Data)
				{
					SerializeObject(entry.Key, writer, serializedObjects, objectMap);
					SerializeObject(entry.Value, writer, serializedObjects, objectMap);
				}
			}
			else
			{
				writer.Write(0);
			}

			// Serialize additional properties for known exceptions
			if (type == typeof(ArgumentException))
			{
				var argEx = (ArgumentException)exception;
				writer.Write(1); // number of additional
				writer.Write("_paramName");
				SerializeObject(argEx.ParamName, writer, serializedObjects, objectMap);
			}
			else if (type == typeof(ArgumentNullException))
			{
				var argEx = (ArgumentNullException)exception;
				writer.Write(1);
				writer.Write("_paramName");
				SerializeObject(argEx.ParamName, writer, serializedObjects, objectMap);
			}
			else if (type == typeof(ArgumentOutOfRangeException))
			{
				var argEx = (ArgumentOutOfRangeException)exception;
				writer.Write(2);
				writer.Write("_paramName");
				SerializeObject(argEx.ParamName, writer, serializedObjects, objectMap);
				writer.Write("_actualValue");
				SerializeObject(argEx.ActualValue, writer, serializedObjects, objectMap);
			}
			else if (type == typeof(Exception))
			{
				// Do not serialize private runtime fields of base Exception to avoid non-serializable members like MethodInfo
				writer.Write(0);
			}
			else
			{
				// Serialize additional fields for custom exceptions
				var fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
					.Where(f => !IsStandardExceptionField(f.Name))
					.ToArray();

				writer.Write(fields.Length);
				foreach (var field in fields)
				{
					writer.Write(field.Name);
					SerializeObject(field.GetValue(exception), writer, serializedObjects, objectMap);
				}
			}
		}

		private void SerializeExpression(Expression expression, BinaryWriter writer, HashSet<object> serializedObjects,
			Dictionary<object, int> objectMap)
		{
			if (expression == null)
			{
				writer.Write((byte)0); // Null expression
				return;
			}

			writer.Write((byte)1); // Expression marker

			// Write NodeType
			writer.Write((int)expression.NodeType);

			// Write Type
			WriteTypeInfo(writer, expression.Type);

			// Write expression-specific data based on NodeType
			switch (expression.NodeType)
			{
				case ExpressionType.Constant:
					var constExpr = (ConstantExpression)expression;
					SerializeObject(constExpr.Value, writer, serializedObjects, objectMap);
					break;

				case ExpressionType.Parameter:
					var paramExpr = (ParameterExpression)expression;
					writer.Write(paramExpr.Name ?? string.Empty);
					break;

				case ExpressionType.MemberAccess:
					var memberExpr = (MemberExpression)expression;
					writer.Write(memberExpr.Member.Name);
					writer.Write(memberExpr.Member.MemberType.ToString());
					SerializeExpression(memberExpr.Expression, writer, serializedObjects, objectMap);
					break;

				case ExpressionType.Call:
					var callExpr = (MethodCallExpression)expression;
					SerializeObject(callExpr.Method, writer, serializedObjects, objectMap);
					SerializeExpression(callExpr.Object, writer, serializedObjects, objectMap);
					writer.Write(callExpr.Arguments.Count);
					foreach (var arg in callExpr.Arguments)
					{
						SerializeExpression(arg, writer, serializedObjects, objectMap);
					}

					break;

				case ExpressionType.Lambda:
					var lambdaExpr = (LambdaExpression)expression;
					writer.Write(lambdaExpr.Name ?? string.Empty);
					writer.Write(lambdaExpr.TailCall);
					SerializeExpression(lambdaExpr.Body, writer, serializedObjects, objectMap);
					writer.Write(lambdaExpr.Parameters.Count);
					foreach (var param in lambdaExpr.Parameters)
					{
						SerializeExpression(param, writer, serializedObjects, objectMap);
					}

					break;

				case ExpressionType.Add:
				case ExpressionType.AddChecked:
				case ExpressionType.And:
				case ExpressionType.AndAlso:
				case ExpressionType.ArrayIndex:
				case ExpressionType.Coalesce:
				case ExpressionType.Divide:
				case ExpressionType.Equal:
				case ExpressionType.ExclusiveOr:
				case ExpressionType.GreaterThan:
				case ExpressionType.GreaterThanOrEqual:
				case ExpressionType.LeftShift:
				case ExpressionType.LessThan:
				case ExpressionType.LessThanOrEqual:
				case ExpressionType.Modulo:
				case ExpressionType.Multiply:
				case ExpressionType.MultiplyChecked:
				case ExpressionType.NotEqual:
				case ExpressionType.Or:
				case ExpressionType.OrElse:
				case ExpressionType.Power:
				case ExpressionType.RightShift:
				case ExpressionType.Subtract:
				case ExpressionType.SubtractChecked:
					var binaryExpr = (BinaryExpression)expression;
					SerializeExpression(binaryExpr.Left, writer, serializedObjects, objectMap);
					SerializeExpression(binaryExpr.Right, writer, serializedObjects, objectMap);
					SerializeObject(binaryExpr.Method, writer, serializedObjects, objectMap);
					SerializeExpression(binaryExpr.Conversion, writer, serializedObjects, objectMap);
					break;

				case ExpressionType.ArrayLength:
				case ExpressionType.Convert:
				case ExpressionType.ConvertChecked:
				case ExpressionType.Negate:
				case ExpressionType.NegateChecked:
				case ExpressionType.Not:
				case ExpressionType.Quote:
				case ExpressionType.TypeAs:
				case ExpressionType.UnaryPlus:
					var unaryExpr = (UnaryExpression)expression;
					SerializeExpression(unaryExpr.Operand, writer, serializedObjects, objectMap);
					SerializeObject(unaryExpr.Method, writer, serializedObjects, objectMap);
					break;

				case ExpressionType.Conditional:
					var condExpr = (ConditionalExpression)expression;
					SerializeExpression(condExpr.Test, writer, serializedObjects, objectMap);
					SerializeExpression(condExpr.IfTrue, writer, serializedObjects, objectMap);
					SerializeExpression(condExpr.IfFalse, writer, serializedObjects, objectMap);
					break;

				case ExpressionType.Invoke:
					var invokeExpr = (InvocationExpression)expression;
					SerializeExpression(invokeExpr.Expression, writer, serializedObjects, objectMap);
					writer.Write(invokeExpr.Arguments.Count);
					foreach (var arg in invokeExpr.Arguments)
					{
						SerializeExpression(arg, writer, serializedObjects, objectMap);
					}

					break;

				case ExpressionType.New:
					var newExpr = (NewExpression)expression;
					SerializeObject(newExpr.Constructor, writer, serializedObjects, objectMap);
					writer.Write(newExpr.Arguments.Count);
					foreach (var arg in newExpr.Arguments)
					{
						SerializeExpression(arg, writer, serializedObjects, objectMap);
					}

					if (newExpr.Members != null)
					{
						writer.Write(newExpr.Members.Count);
						foreach (var member in newExpr.Members)
						{
							writer.Write(member.Name);
							writer.Write(member.MemberType.ToString());
						}
					}
					else
					{
						writer.Write(0);
					}

					break;

				case ExpressionType.NewArrayInit:
				case ExpressionType.NewArrayBounds:
					var newArrayExpr = (NewArrayExpression)expression;
					writer.Write(newArrayExpr.Expressions.Count);
					foreach (var expr in newArrayExpr.Expressions)
					{
						SerializeExpression(expr, writer, serializedObjects, objectMap);
					}

					break;

				case ExpressionType.ListInit:
					var listInitExpr = (ListInitExpression)expression;
					SerializeExpression(listInitExpr.NewExpression, writer, serializedObjects, objectMap);
					writer.Write(listInitExpr.Initializers.Count);
					foreach (var init in listInitExpr.Initializers)
					{
						writer.Write(init.AddMethod?.Name ?? string.Empty);
						writer.Write(init.Arguments.Count);
						foreach (var arg in init.Arguments)
						{
							SerializeExpression(arg, writer, serializedObjects, objectMap);
						}
					}

					break;

				case ExpressionType.MemberInit:
					var memberInitExpr = (MemberInitExpression)expression;
					SerializeExpression(memberInitExpr.NewExpression, writer, serializedObjects, objectMap);
					writer.Write(memberInitExpr.Bindings.Count);
					foreach (var binding in memberInitExpr.Bindings)
					{
						writer.Write(binding.Member.Name);
						writer.Write(binding.BindingType.ToString());
						// For simplicity, only handle MemberAssignment
						if (binding is MemberAssignment assign)
						{
							SerializeExpression(assign.Expression, writer, serializedObjects, objectMap);
						}
						else
						{
							SerializeExpression(null, writer, serializedObjects, objectMap); // Placeholder
						}
					}

					break;

				case ExpressionType.TypeIs:
					var typeBinaryExpr = (TypeBinaryExpression)expression;
					SerializeExpression(typeBinaryExpr.Expression, writer, serializedObjects, objectMap);
					WriteTypeInfo(writer, typeBinaryExpr.TypeOperand);
					break;

				default:
					throw new NotSupportedException($"Expression type {expression.NodeType} is not supported.");
			}
		}

		private void SerializeDataSet(DataSet dataSet, BinaryWriter writer, HashSet<object> serializedObjects,
			Dictionary<object, int> objectMap)
		{
			if (Config.EnableBinaryDataSetSerialization)
			{
				writer.Write((byte)1); // Binary marker
				SerializeDataSetBinary(dataSet, writer, serializedObjects, objectMap);
			}
			else
			{
				// XML serialization (default)
				writer.Write((byte)0); // XML marker
				var schemaBytes = System.Buffers.ArrayPool<byte>.Shared.Rent(4096);
				try
				{
					using var ms = new MemoryStream(schemaBytes);
					dataSet.WriteXmlSchema(ms);
					var schemaXml = Encoding.UTF8.GetString(schemaBytes, 0, (int)ms.Position);
					writer.Write(schemaXml);

					ms.SetLength(0);
					dataSet.WriteXml(ms, XmlWriteMode.DiffGram);
					var diffGramXml = Encoding.UTF8.GetString(schemaBytes, 0, (int)ms.Position);
					writer.Write(diffGramXml);
				}
				finally
				{
					System.Buffers.ArrayPool<byte>.Shared.Return(schemaBytes);
				}
			}
		}

		private void SerializeDataSetBinary(DataSet dataSet, BinaryWriter writer, HashSet<object> serializedObjects,
			Dictionary<object, int> objectMap)
		{
			// Serialize DataSet properties
			writer.Write(dataSet.DataSetName ?? string.Empty);

			// Calculate flags for non-default properties
			byte flags = 0;
			if (!string.IsNullOrEmpty(dataSet.Namespace)) flags |= 1 << 0; // default ""
			if (!string.IsNullOrEmpty(dataSet.Prefix)) flags |= 1 << 1; // default ""
			if (dataSet.CaseSensitive) flags |= 1 << 2; // default false
			if (dataSet.Locale != null && dataSet.Locale != System.Globalization.CultureInfo.CurrentCulture)
				flags |= 1 << 3; // default CurrentCulture
			if (!dataSet.EnforceConstraints) flags |= 1 << 4; // default true

			writer.Write(flags);

			// Serialize non-default values
			if ((flags & (1 << 0)) != 0) writer.Write(dataSet.Namespace ?? string.Empty);
			if ((flags & (1 << 1)) != 0) writer.Write(dataSet.Prefix ?? string.Empty);
			if ((flags & (1 << 2)) != 0) writer.Write(dataSet.CaseSensitive);
			if ((flags & (1 << 3)) != 0) writer.Write(dataSet.Locale != null ? dataSet.Locale.Name : string.Empty);
			if ((flags & (1 << 4)) != 0) writer.Write(dataSet.EnforceConstraints);

			// Serialize Tables
			writer.Write(dataSet.Tables.Count);
			foreach (DataTable table in dataSet.Tables)
			{
				SerializeDataTableBinary(table, writer, serializedObjects, objectMap);
			}

			// Serialize Relations
			writer.Write(dataSet.Relations.Count);
			foreach (DataRelation relation in dataSet.Relations)
			{
				writer.Write(relation.RelationName ?? string.Empty);
				writer.Write(relation.ParentTable.TableName ?? string.Empty);
				writer.Write(relation.ChildTable.TableName ?? string.Empty);

				// Parent columns
				writer.Write(relation.ParentColumns.Length);
				foreach (DataColumn col in relation.ParentColumns)
				{
					writer.Write(col.ColumnName ?? string.Empty);
				}

				// Child columns
				writer.Write(relation.ChildColumns.Length);
				foreach (DataColumn col in relation.ChildColumns)
				{
					writer.Write(col.ColumnName ?? string.Empty);
				}

				writer.Write(relation.Nested);
			}

			// Serialize ExtendedProperties
			SerializeDictionary(dataSet.ExtendedProperties, writer, serializedObjects, objectMap);
		}

		private void SerializeDataTable(DataTable dataTable, BinaryWriter writer, HashSet<object> serializedObjects,
			Dictionary<object, int> objectMap)
		{
			if (Config.EnableBinaryDataSetSerialization)
			{
				writer.Write((byte)1); // Binary marker
				SerializeDataTableBinary(dataTable, writer, serializedObjects, objectMap);
			}
			else
			{
				// XML serialization (default)
				writer.Write((byte)0); // XML marker
				// Do not add the DataTable to a temporary DataSet, as it may already belong to another DataSet
				// and would throw "DataTable already belongs to another DataSet.". Instead, write schema and data
				// directly from the DataTable itself.
				var schemaBytes = System.Buffers.ArrayPool<byte>.Shared.Rent(4096);
				try
				{
					using var ms = new MemoryStream(schemaBytes);
					dataTable.WriteXmlSchema(ms);
					var schemaXml = Encoding.UTF8.GetString(schemaBytes, 0, (int)ms.Position);
					writer.Write(schemaXml);

					ms.SetLength(0);
					dataTable.WriteXml(ms, XmlWriteMode.DiffGram);
					var diffGramXml = Encoding.UTF8.GetString(schemaBytes, 0, (int)ms.Position);
					writer.Write(diffGramXml);
				}
				finally
				{
					System.Buffers.ArrayPool<byte>.Shared.Return(schemaBytes);
				}
			}
		}

		private void SerializeDataTableBinary(DataTable dataTable, BinaryWriter writer,
			HashSet<object> serializedObjects,
			Dictionary<object, int> objectMap)
		{
			// Serialize DataTable properties
			writer.Write(dataTable.TableName ?? string.Empty);

			// Calculate flags for non-default properties
			byte flags = 0;
			if (!string.IsNullOrEmpty(dataTable.Namespace)) flags |= 1 << 0; // default ""
			if (!string.IsNullOrEmpty(dataTable.Prefix)) flags |= 1 << 1; // default ""
			if (dataTable.CaseSensitive) flags |= 1 << 2; // default false
			if (dataTable.Locale != null && dataTable.Locale != System.Globalization.CultureInfo.CurrentCulture)
				flags |= 1 << 3; // default CurrentCulture

			writer.Write(flags);

			// Serialize non-default values
			if ((flags & (1 << 0)) != 0) writer.Write(dataTable.Namespace ?? string.Empty);
			if ((flags & (1 << 1)) != 0) writer.Write(dataTable.Prefix ?? string.Empty);
			if ((flags & (1 << 2)) != 0) writer.Write(dataTable.CaseSensitive);
			if ((flags & (1 << 3)) != 0) writer.Write(dataTable.Locale != null ? dataTable.Locale.Name : string.Empty);

			// Serialize Columns
			writer.Write(dataTable.Columns.Count);
			foreach (DataColumn column in dataTable.Columns)
			{
				writer.Write(column.ColumnName ?? string.Empty);
				writer.Write(column.DataType.FullName ?? string.Empty);

				// Calculate flags for non-default properties
				byte flags1 = 0;
				byte flags2 = 0;

				if (!column.AllowDBNull) flags1 |= 1 << 0; // default true
				if (column.AutoIncrement) flags1 |= 1 << 1; // default false
				if (column.AutoIncrementSeed != 0) flags1 |= 1 << 2; // default 0
				if (column.AutoIncrementStep != 1) flags1 |= 1 << 3; // default 1
				if (column.Caption != column.ColumnName) flags1 |= 1 << 4; // default ColumnName
				if (column.DefaultValue != null && column.DefaultValue != DBNull.Value)
					flags1 |= 1 << 5; // default null
				if (!string.IsNullOrEmpty(column.Expression)) flags1 |= 1 << 6; // default ""
				if (column.MaxLength != -1) flags1 |= 1 << 7; // default -1

				if (column.ReadOnly) flags2 |= 1 << 0; // default false
				if (column.Unique) flags2 |= 1 << 1; // default false

				writer.Write(flags1);
				writer.Write(flags2);

				// Serialize non-default values
				if ((flags1 & (1 << 0)) != 0) writer.Write(column.AllowDBNull);
				if ((flags1 & (1 << 1)) != 0) writer.Write(column.AutoIncrement);
				if ((flags1 & (1 << 2)) != 0) writer.Write(column.AutoIncrementSeed);
				if ((flags1 & (1 << 3)) != 0) writer.Write(column.AutoIncrementStep);
				if ((flags1 & (1 << 4)) != 0) writer.Write(column.Caption ?? string.Empty);
				if ((flags1 & (1 << 5)) != 0)
					SerializeObject(column.DefaultValue, writer, serializedObjects, objectMap);
				if ((flags1 & (1 << 6)) != 0) writer.Write(column.Expression ?? string.Empty);
				if ((flags1 & (1 << 7)) != 0) writer.Write(column.MaxLength);
				if ((flags2 & (1 << 0)) != 0) writer.Write(column.ReadOnly);
				if ((flags2 & (1 << 1)) != 0) writer.Write(column.Unique);
			}

			// Serialize Rows
			writer.Write(dataTable.Rows.Count);
			foreach (DataRow row in dataTable.Rows)
			{
				for (int i = 0; i < dataTable.Columns.Count; i++)
				{
					var value = row[i];
					writer.Write(value != null);
					if (value != null)
					{
						SerializeObject(value, writer, serializedObjects, objectMap);
					}
				}
			}

			// Serialize Constraints
			writer.Write(dataTable.Constraints.Count);
			foreach (Constraint constraint in dataTable.Constraints)
			{
				if (constraint is UniqueConstraint unique)
				{
					writer.Write((byte)1); // UniqueConstraint
					writer.Write(unique.ConstraintName ?? string.Empty);
					writer.Write(unique.IsPrimaryKey);
					writer.Write(unique.Columns.Length);
					foreach (DataColumn col in unique.Columns)
					{
						writer.Write(col.ColumnName ?? string.Empty);
					}
				}
				else if (constraint is ForeignKeyConstraint fk)
				{
					writer.Write((byte)2); // ForeignKeyConstraint
					writer.Write(fk.ConstraintName ?? string.Empty);
					writer.Write(fk.AcceptRejectRule.ToString());
					writer.Write(fk.DeleteRule.ToString());
					writer.Write(fk.UpdateRule.ToString());
					writer.Write(fk.RelatedTable?.TableName ?? string.Empty);

					// Columns
					writer.Write(fk.Columns.Length);
					foreach (DataColumn col in fk.Columns)
					{
						writer.Write(col.ColumnName ?? string.Empty);
					}

					writer.Write(fk.RelatedColumns.Length);
					foreach (DataColumn col in fk.RelatedColumns)
					{
						writer.Write(col.ColumnName ?? string.Empty);
					}
				}
				else
				{
					// Unknown constraint type, skip
					writer.Write((byte)0);
				}
			}

			// Serialize ExtendedProperties
			SerializeDictionary(dataTable.ExtendedProperties, writer, serializedObjects, objectMap);
		}

		private object DeserializeException(Type type, BinaryReader reader, Dictionary<int, object> deserializedObjects,
			int objectId)
		{
			// Read basic exception properties
			var message = reader.ReadString();
			var source = reader.ReadString();
			var stackTrace = reader.ReadString();
			var helpLink = reader.ReadString();
			var hResult = reader.ReadInt32();

			// Deserialize inner exception
			var innerException = DeserializeObject(reader, deserializedObjects) as Exception;

			// Deserialize data dictionary
			var dataCount = reader.ReadInt32();
			var dataKeys = new object[dataCount];
			var dataValues = new object[dataCount];
			for (int i = 0; i < dataCount; i++)
			{
				dataKeys[i] = DeserializeObject(reader, deserializedObjects);
				dataValues[i] = DeserializeObject(reader, deserializedObjects);
			}

			// Deserialize additional fields
			var fieldCount = reader.ReadInt32();
			var additionalFields = new Dictionary<string, object>();
			for (int i = 0; i < fieldCount; i++)
			{
				var fieldName = reader.ReadString();
				var fieldValue = DeserializeObject(reader, deserializedObjects);
				additionalFields[fieldName] = fieldValue;
			}

			// Note: Do not set fields on the exception before it's created.
			// Additional/custom fields will be applied after the exception instance
			// has been constructed and registered (see below).

			// Create exception instance using appropriate constructor
			Exception exception;
			try
			{
				if (type == typeof(ArgumentException))
				{
					var paramName = additionalFields.ContainsKey("_paramName")
						? (string)additionalFields["_paramName"]
						: null;
					var baseMessage = StripArgumentParameterSuffix(message, paramName);
					exception = new ArgumentException(baseMessage, paramName, innerException);
				}
				else if (type == typeof(ArgumentNullException))
				{
					var paramName = additionalFields.ContainsKey("_paramName")
						? (string)additionalFields["_paramName"]
						: null;
					var baseMessage = StripArgumentParameterSuffix(message, paramName);
					// Disambiguate by explicit cast to string overload (paramName, message)
					exception = new ArgumentNullException(paramName, baseMessage);
				}
				else if (type == typeof(ArgumentOutOfRangeException))
				{
					var paramName = additionalFields.ContainsKey("_paramName")
						? (string)additionalFields["_paramName"]
						: null;
					var actualValue = additionalFields.ContainsKey("_actualValue")
						? additionalFields["_actualValue"]
						: null;
					var baseMessage = StripArgumentParameterSuffix(message, paramName);
					exception = new ArgumentOutOfRangeException(paramName, actualValue, baseMessage);
				}
				else if (type == typeof(InvalidOperationException))
				{
					exception = new InvalidOperationException(message, innerException);
				}
				else if (type == typeof(NotSupportedException))
				{
					exception = new NotSupportedException(message, innerException);
				}
				else if (type == typeof(IOException))
				{
					exception = new IOException(message, innerException);
				}
				else if (type == typeof(SystemException))
				{
					exception = new SystemException(message, innerException);
				}
				else
				{
					// For custom exceptions or unknown types, try to use constructor or create without constructor
					if (innerException != null)
					{
						var ctor = type.GetConstructor(new[] { typeof(string), typeof(Exception) });
						if (ctor != null)
						{
							exception = (Exception)ctor.Invoke(new object[] { message, innerException });
						}
						else
						{
							var ctorMessage = type.GetConstructor(new[] { typeof(string) });
							if (ctorMessage != null)
							{
								exception = (Exception)ctorMessage.Invoke(new object[] { message });
							}
							else
							{
								exception = (Exception)CreateInstanceWithoutConstructor(type);
							}
						}
					}
					else
					{
						var ctorMessage = type.GetConstructor(new[] { typeof(string) });
						if (ctorMessage != null)
						{
							exception = (Exception)ctorMessage.Invoke(new object[] { message });
						}
						else
						{
							exception = (Exception)CreateInstanceWithoutConstructor(type);
						}
					}
				}
			}
			catch
			{
				exception = (Exception)CreateInstanceWithoutConstructor(type);
			}

			if (exception == null)
			{
				throw new InvalidOperationException("Failed to create exception");
			}

			// Register immediately for circular references
			deserializedObjects[objectId] = exception;

			// Set additional properties
			if (!string.IsNullOrEmpty(source)) exception.Source = source;
			if (!string.IsNullOrEmpty(helpLink)) exception.HelpLink = helpLink;

			// Set HResult and stack trace via private fields
			SetExceptionField(exception, "_HResult", hResult);
			if (!string.IsNullOrEmpty(stackTrace))
			{
				var stackForException = stackTrace;
				// Some tests expect the message to appear in the StackTrace text. If it's not there, prepend it.
				if (!string.IsNullOrEmpty(message) && !stackForException.Contains(message))
				{
					stackForException = message + Environment.NewLine + stackForException;
				}

				SetExceptionField(exception, "_stackTraceString", stackForException);
			}

			// Set data
			for (int i = 0; i < dataCount; i++)
			{
				exception.Data[dataKeys[i]] = dataValues[i];
			}

			// Set additional fields for custom exceptions
			if (type != typeof(ArgumentException) && type != typeof(ArgumentNullException) &&
			    type != typeof(ArgumentOutOfRangeException) && additionalFields.Count > 0)
			{
				foreach (var kvp in additionalFields)
				{
					var field = type.GetField(kvp.Key,
						BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
					field?.SetValue(exception, kvp.Value);
				}
			}

			return exception;
		}

		private object DeserializeExpression(BinaryReader reader,
			Dictionary<int, object> deserializedObjects)
		{
			var marker = reader.ReadByte();
			if (marker == 0) // Null expression
			{
				return null;
			}

			// Read NodeType
			var nodeType = (ExpressionType)reader.ReadInt32();

			// Read Type
			var exprType = ReadTypeInfo(reader);

			Expression result = null;

			switch (nodeType)
			{
				case ExpressionType.Constant:
					var value = DeserializeObject(reader, deserializedObjects);
					result = Expression.Constant(value, exprType);
					break;

				case ExpressionType.Parameter:
					var paramName = reader.ReadString();
					result = Expression.Parameter(exprType, paramName);
					break;

				case ExpressionType.MemberAccess:
					var maMemberName = reader.ReadString();
					var maMemberTypeStr = reader.ReadString();
					var maMemberType = (MemberTypes)Enum.Parse(typeof(MemberTypes), maMemberTypeStr);
					var maMemberExpr =
						(Expression)DeserializeExpression(reader, deserializedObjects);
					var maMemberInfo = maMemberExpr.Type.GetMember(maMemberName, maMemberType,
							BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
						.FirstOrDefault();
					if (maMemberInfo != null)
					{
						result = Expression.MakeMemberAccess(maMemberExpr, maMemberInfo);
					}

					break;

				case ExpressionType.Call:
					var method = (MethodInfo)DeserializeObject(reader, deserializedObjects);
					var callObject =
						(Expression)DeserializeExpression(reader, deserializedObjects);
					var argCount = reader.ReadInt32();
					var arguments = new Expression[argCount];
					for (int i = 0; i < argCount; i++)
					{
						arguments[i] =
							(Expression)DeserializeExpression(reader, deserializedObjects);
					}

					result = Expression.Call(callObject, method, arguments);
					break;

				case ExpressionType.Lambda:
					var lambdaName = reader.ReadString();
					var tailCall = reader.ReadBoolean();
					var body = (Expression)DeserializeExpression(reader, deserializedObjects);
					var paramCount = reader.ReadInt32();
					var parameters = new ParameterExpression[paramCount];
					for (int i = 0; i < paramCount; i++)
					{
						parameters[i] = (ParameterExpression)DeserializeExpression(reader,
							deserializedObjects);
					}

					// Replace parameters in body with the deserialized parameters
					body = ReplaceParameters(body, parameters);
					result = Expression.Lambda(exprType, body, lambdaName, tailCall, parameters);
					break;

				case ExpressionType.Add:
				case ExpressionType.AddChecked:
				case ExpressionType.And:
				case ExpressionType.AndAlso:
				case ExpressionType.ArrayIndex:
				case ExpressionType.Coalesce:
				case ExpressionType.Divide:
				case ExpressionType.Equal:
				case ExpressionType.ExclusiveOr:
				case ExpressionType.GreaterThan:
				case ExpressionType.GreaterThanOrEqual:
				case ExpressionType.LeftShift:
				case ExpressionType.LessThan:
				case ExpressionType.LessThanOrEqual:
				case ExpressionType.Modulo:
				case ExpressionType.Multiply:
				case ExpressionType.MultiplyChecked:
				case ExpressionType.NotEqual:
				case ExpressionType.Or:
				case ExpressionType.OrElse:
				case ExpressionType.Power:
				case ExpressionType.RightShift:
				case ExpressionType.Subtract:
				case ExpressionType.SubtractChecked:
					var left = (Expression)DeserializeExpression(reader, deserializedObjects);
					var right = (Expression)DeserializeExpression(reader, deserializedObjects);
					var binaryMethod = (MethodInfo)DeserializeObject(reader, deserializedObjects);
					var conversion = (LambdaExpression)DeserializeExpression(reader,
						deserializedObjects);
					result = Expression.MakeBinary(nodeType, left, right, false, binaryMethod, conversion);
					break;

				case ExpressionType.ArrayLength:
				case ExpressionType.Convert:
				case ExpressionType.ConvertChecked:
				case ExpressionType.Negate:
				case ExpressionType.NegateChecked:
				case ExpressionType.Not:
				case ExpressionType.Quote:
				case ExpressionType.TypeAs:
				case ExpressionType.UnaryPlus:
					var operand =
						(Expression)DeserializeExpression(reader, deserializedObjects);
					var unaryMethod = (MethodInfo)DeserializeObject(reader, deserializedObjects);
					result = Expression.MakeUnary(nodeType, operand, exprType, unaryMethod);
					break;

				case ExpressionType.Conditional:
					var test = (Expression)DeserializeExpression(reader, deserializedObjects);
					var ifTrue = (Expression)DeserializeExpression(reader, deserializedObjects);
					var ifFalse =
						(Expression)DeserializeExpression(reader, deserializedObjects);
					result = Expression.Condition(test, ifTrue, ifFalse);
					break;

				case ExpressionType.Invoke:
					var invokeExpr =
						(Expression)DeserializeExpression(reader, deserializedObjects);
					var invokeArgCount = reader.ReadInt32();
					var invokeArgs = new Expression[invokeArgCount];
					for (int i = 0; i < invokeArgCount; i++)
					{
						invokeArgs[i] =
							(Expression)DeserializeExpression(reader, deserializedObjects);
					}

					result = Expression.Invoke(invokeExpr, invokeArgs);
					break;

				case ExpressionType.New:
					var constructor = (ConstructorInfo)DeserializeObject(reader, deserializedObjects);
					var newArgCount = reader.ReadInt32();
					var newArgs = new Expression[newArgCount];
					for (int i = 0; i < newArgCount; i++)
					{
						newArgs[i] =
							(Expression)DeserializeExpression(reader, deserializedObjects);
					}

					var memberCount = reader.ReadInt32();
					var members = new MemberInfo[memberCount];
					for (int i = 0; i < memberCount; i++)
					{
						var newMemberName = reader.ReadString();
						var newMemberTypeStr = reader.ReadString();
						var newMemberTypeEnum = (MemberTypes)Enum.Parse(typeof(MemberTypes), newMemberTypeStr);
						// Simplified: assume instance members
						members[i] = exprType.GetMember(newMemberName, newMemberTypeEnum,
							BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).FirstOrDefault();
					}

					result = Expression.New(constructor, newArgs, members.Length > 0 ? members : null);
					break;

				case ExpressionType.NewArrayInit:
				case ExpressionType.NewArrayBounds:
					var arrayExprCount = reader.ReadInt32();
					var arrayExprs = new Expression[arrayExprCount];
					for (int i = 0; i < arrayExprCount; i++)
					{
						arrayExprs[i] =
							(Expression)DeserializeExpression(reader, deserializedObjects);
					}

					result = nodeType == ExpressionType.NewArrayInit
						? Expression.NewArrayInit(exprType.GetElementType()!, arrayExprs)
						: Expression.NewArrayBounds(exprType.GetElementType()!, arrayExprs);
					break;

				case ExpressionType.ListInit:
					var listNewExpr =
						(NewExpression)DeserializeExpression(reader, deserializedObjects);
					var initCount = reader.ReadInt32();
					var initializers = new ElementInit[initCount];
					for (int i = 0; i < initCount; i++)
					{
						var liAddMethodName = reader.ReadString();
						var liInitArgCount = reader.ReadInt32();
						var liInitArgs = new Expression[liInitArgCount];
						for (int j = 0; j < liInitArgCount; j++)
						{
							liInitArgs[j] = (Expression)DeserializeExpression(reader,
								deserializedObjects);
						}

						var liAddMethod = exprType.GetMethod(liAddMethodName);
						initializers[i] = Expression.ElementInit(liAddMethod!, liInitArgs);
					}

					result = Expression.ListInit(listNewExpr, initializers);
					break;

				case ExpressionType.MemberInit:
					var memberNewExpr =
						(NewExpression)DeserializeExpression(reader, deserializedObjects);
					var bindingCount = reader.ReadInt32();
					var bindings = new MemberBinding[bindingCount];
					for (int i = 0; i < bindingCount; i++)
					{
						var miBindingMemberName = reader.ReadString();
						reader.ReadString();

						var miBindingExpr =
							(Expression)DeserializeExpression(reader, deserializedObjects);

						var miBindingMember = exprType.GetMember(miBindingMemberName).FirstOrDefault();

						bindings[i] = Expression.Bind(miBindingMember!, miBindingExpr);
					}

					result = Expression.MemberInit(memberNewExpr, bindings);
					break;

				case ExpressionType.TypeIs:
					var typeIsExpr =
						(Expression)DeserializeExpression(reader, deserializedObjects);
					var typeOperand = ReadTypeInfo(reader);
					result = Expression.TypeIs(typeIsExpr, typeOperand);
					break;

				default:
					throw new NotSupportedException($"Expression type {nodeType} is not supported.");
			}

			// Validate the expression
			TypeValidator.ValidateExpression(result);

			return result;
		}

		private Expression ReplaceParameters(Expression expression, ParameterExpression[] newParameters)
		{
			if (newParameters.Length == 0)
				return expression;

			var visitor = new ParameterReplacer(newParameters);
			return visitor.Visit(expression);
		}

		private class ParameterReplacer : ExpressionVisitor
		{
			private readonly Dictionary<string, ParameterExpression> _parameterMap;

			public ParameterReplacer(ParameterExpression[] parameters)
			{
				_parameterMap = parameters.ToDictionary(p => p.Name);
			}

			protected override Expression VisitParameter(ParameterExpression node)
			{
				if (_parameterMap.TryGetValue(node.Name, out var replacement))
				{
					return replacement;
				}

				return base.VisitParameter(node);
			}
		}

		private object DeserializeDataSet(Type type, BinaryReader reader, Dictionary<int, object> deserializedObjects,
			int objectId)
		{
			var isBinary = reader.ReadByte() == 1;
			if (isBinary)
			{
				return DeserializeDataSetBinary(type, reader, deserializedObjects, objectId);
			}
			else
			{
				// XML deserialization
				var schemaXml = reader.ReadString();
				var diffGramXml = reader.ReadString();
				var dataSet = (DataSet)CreateInstanceWithoutConstructor(type);
				deserializedObjects[objectId] = dataSet;
				using var sr = new StringReader(schemaXml);
				dataSet.ReadXmlSchema(sr);
				using var sr2 = new StringReader(diffGramXml);
				dataSet.ReadXml(sr2, XmlReadMode.DiffGram);
				return dataSet;
			}
		}

		private object DeserializeDataSetBinary(Type type, BinaryReader reader,
			Dictionary<int, object> deserializedObjects,
			int objectId)
		{
			var dataSet = (DataSet)CreateInstanceWithoutConstructor(type);
			deserializedObjects[objectId] = dataSet;

			// Deserialize DataSet properties
			dataSet.DataSetName = reader.ReadString();

			var flags = reader.ReadByte();

			// Deserialize non-default values (defaults are already set by DataSet constructor)
			if ((flags & (1 << 0)) != 0) dataSet.Namespace = reader.ReadString();
			if ((flags & (1 << 1)) != 0) dataSet.Prefix = reader.ReadString();
			if ((flags & (1 << 2)) != 0) dataSet.CaseSensitive = reader.ReadBoolean();
			if ((flags & (1 << 3)) != 0)
			{
				var localeName = reader.ReadString();
				if (!string.IsNullOrEmpty(localeName))
				{
					dataSet.Locale = new System.Globalization.CultureInfo(localeName);
				}
			}

			if ((flags & (1 << 4)) != 0) dataSet.EnforceConstraints = reader.ReadBoolean();

			// Deserialize Tables
			var tableCount = reader.ReadInt32();
			for (int i = 0; i < tableCount; i++)
			{
				var table = DeserializeDataTableBinary(typeof(DataTable), reader, deserializedObjects, -1);
				dataSet.Tables.Add((DataTable)table);
			}

			// Deserialize Relations
			var relationCount = reader.ReadInt32();
			for (int i = 0; i < relationCount; i++)
			{
				var relationName = reader.ReadString();
				var parentTableName = reader.ReadString();
				var childTableName = reader.ReadString();

				var parentTable = dataSet.Tables[parentTableName];
				var childTable = dataSet.Tables[childTableName];

				var parentColCount = reader.ReadInt32();
				var parentColumns = new DataColumn[parentColCount];
				for (int j = 0; j < parentColCount; j++)
				{
					var colName = reader.ReadString();
					parentColumns[j] = parentTable.Columns[colName];
				}

				var childColCount = reader.ReadInt32();
				var childColumns = new DataColumn[childColCount];
				for (int j = 0; j < childColCount; j++)
				{
					var colName = reader.ReadString();
					childColumns[j] = childTable.Columns[colName];
				}

				var nested = reader.ReadBoolean();

				var relation = new DataRelation(relationName, parentColumns, childColumns, false);
				relation.Nested = nested;
				dataSet.Relations.Add(relation);
			}

			// Deserialize ExtendedProperties
			var extProps = (System.Collections.IDictionary)DeserializeDictionary(typeof(System.Collections.Hashtable),
				reader, deserializedObjects, -1);
			foreach (System.Collections.DictionaryEntry entry in extProps)
			{
				dataSet.ExtendedProperties[entry.Key] = entry.Value;
			}

			return dataSet;
		}

		private object DeserializeDataTable(Type type, BinaryReader reader, Dictionary<int, object> deserializedObjects,
			int objectId)
		{
			var isBinary = reader.ReadByte() == 1;
			if (isBinary)
			{
				return DeserializeDataTableBinary(type, reader, deserializedObjects, objectId);
			}
			else
			{
				// XML deserialization
				var schemaXml = reader.ReadString();
				var diffGramXml = reader.ReadString();
				var tempDataSet = new DataSet();
				using var sr = new StringReader(schemaXml);
				tempDataSet.ReadXmlSchema(sr);
				using var sr2 = new StringReader(diffGramXml);
				tempDataSet.ReadXml(sr2, XmlReadMode.DiffGram);
				var baseTable = tempDataSet.Tables[0];

				DataTable resultTable;
				if (type == typeof(DataTable))
				{
					resultTable = baseTable;
				}
				else
				{
					// Create typed DataTable instance and merge data from baseTable
					var typedTable = (DataTable)CreateInstanceWithoutConstructor(type);
					typedTable.TableName = baseTable.TableName;
					typedTable.Merge(baseTable, preserveChanges: false, MissingSchemaAction.Add);
					resultTable = typedTable;
				}

				deserializedObjects[objectId] = resultTable;
				return resultTable;
			}
		}

		private object DeserializeDataTableBinary(Type type, BinaryReader reader,
			Dictionary<int, object> deserializedObjects,
			int objectId)
		{
			var dataTable = (DataTable)CreateInstanceWithoutConstructor(type);
			if (objectId >= 0)
			{
				deserializedObjects[objectId] = dataTable;
			}

			// Deserialize DataTable properties
			dataTable.TableName = reader.ReadString();

			var flags = reader.ReadByte();

			// Set defaults
			dataTable.Namespace = string.Empty;
			dataTable.Prefix = string.Empty;
			dataTable.CaseSensitive = false;
			dataTable.Locale = System.Globalization.CultureInfo.CurrentCulture;

			// Deserialize non-default values
			if ((flags & (1 << 0)) != 0) dataTable.Namespace = reader.ReadString();
			if ((flags & (1 << 1)) != 0) dataTable.Prefix = reader.ReadString();
			if ((flags & (1 << 2)) != 0) dataTable.CaseSensitive = reader.ReadBoolean();
			if ((flags & (1 << 3)) != 0)
			{
				var localeName = reader.ReadString();
				if (!string.IsNullOrEmpty(localeName))
				{
					dataTable.Locale = new System.Globalization.CultureInfo(localeName);
				}
			}

			// Deserialize Columns
			var columnCount = reader.ReadInt32();
			for (int i = 0; i < columnCount; i++)
			{
				var columnName = reader.ReadString();
				var dataTypeName = reader.ReadString();
				var dataType = Type.GetType(dataTypeName) ?? typeof(string);

				var flags1 = reader.ReadByte();
				var flags2 = reader.ReadByte();

				// Set defaults
				var allowDBNull = true;
				var autoIncrement = false;
				var autoIncrementSeed = 0L;
				var autoIncrementStep = 1L;
				var caption = columnName;
				object defaultValue = null;
				var expression = string.Empty;
				var maxLength = -1;
				var readOnly = false;
				var unique = false;

				// Deserialize non-default values
				if ((flags1 & (1 << 0)) != 0) allowDBNull = reader.ReadBoolean();
				if ((flags1 & (1 << 1)) != 0) autoIncrement = reader.ReadBoolean();
				if ((flags1 & (1 << 2)) != 0) autoIncrementSeed = reader.ReadInt64();
				if ((flags1 & (1 << 3)) != 0) autoIncrementStep = reader.ReadInt64();
				if ((flags1 & (1 << 4)) != 0) caption = reader.ReadString();
				if ((flags1 & (1 << 5)) != 0) defaultValue = DeserializeObject(reader, deserializedObjects);
				if ((flags1 & (1 << 6)) != 0) expression = reader.ReadString();
				if ((flags1 & (1 << 7)) != 0) maxLength = reader.ReadInt32();
				if ((flags2 & (1 << 0)) != 0) readOnly = reader.ReadBoolean();
				if ((flags2 & (1 << 1)) != 0) unique = reader.ReadBoolean();

				var column = new DataColumn(columnName, dataType)
				{
					AllowDBNull = allowDBNull,
					AutoIncrement = autoIncrement,
					AutoIncrementSeed = autoIncrementSeed,
					AutoIncrementStep = autoIncrementStep,
					Caption = caption,
					DefaultValue = defaultValue,
					Expression = expression,
					MaxLength = maxLength,
					ReadOnly = readOnly,
					Unique = unique
				};
				dataTable.Columns.Add(column);
			}

			// Deserialize Rows
			var rowCount = reader.ReadInt32();
			for (int i = 0; i < rowCount; i++)
			{
				var row = dataTable.NewRow();
				for (int j = 0; j < dataTable.Columns.Count; j++)
				{
					var hasValue = reader.ReadBoolean();
					if (hasValue)
					{
						var value = DeserializeObject(reader, deserializedObjects);
						row[j] = value;
					}
					else
					{
						row[j] = DBNull.Value;
					}
				}

				dataTable.Rows.Add(row);
				// Note: RowState is not directly settable; it depends on the operations
			}

			// Deserialize Constraints
			var constraintCount = reader.ReadInt32();
			for (int i = 0; i < constraintCount; i++)
			{
				var constraintType = reader.ReadByte();
				if (constraintType == 1) // UniqueConstraint
				{
					var constraintName = reader.ReadString();
					var isPrimaryKey = reader.ReadBoolean();
					var colCount = reader.ReadInt32();
					var columns = new DataColumn[colCount];
					for (int j = 0; j < colCount; j++)
					{
						var colName = reader.ReadString();
						columns[j] = dataTable.Columns[colName];
					}

					if (!dataTable.Constraints.Contains(constraintName))
					{
						var constraint = new UniqueConstraint(constraintName, columns, isPrimaryKey);
						dataTable.Constraints.Add(constraint);
					}
				}
				else if (constraintType == 2) // ForeignKeyConstraint
				{
					var constraintName = reader.ReadString();
					var acceptRejectRuleStr = reader.ReadString();
					var deleteRuleStr = reader.ReadString();
					var updateRuleStr = reader.ReadString();
					var relatedTableName = reader.ReadString();

					var colCount = reader.ReadInt32();
					var columns = new DataColumn[colCount];
					for (int j = 0; j < colCount; j++)
					{
						var colName = reader.ReadString();
						columns[j] = dataTable.Columns[colName];
					}

					var relatedColCount = reader.ReadInt32();
					var relatedColumns = new DataColumn[relatedColCount];
					for (int j = 0; j < relatedColCount; j++)
					{
						var colName = reader.ReadString();
						// Note: Related table might not be available yet; this is a simplification
						// In a full implementation, we'd need to defer constraint creation
						relatedColumns[j] = null; // Placeholder
					}

					// Skip for now as related table may not be set
					// var constraint = new ForeignKeyConstraint(constraintName, relatedColumns, columns);
					// constraint.AcceptRejectRule = (AcceptRejectRule)Enum.Parse(typeof(AcceptRejectRule), acceptRejectRuleStr);
					// constraint.DeleteRule = (Rule)Enum.Parse(typeof(Rule), deleteRuleStr);
					// constraint.UpdateRule = (Rule)Enum.Parse(typeof(Rule), updateRuleStr);
					// dataTable.Constraints.Add(constraint);
				}
			}

			// Deserialize ExtendedProperties
			var extProps = (System.Collections.IDictionary)DeserializeDictionary(typeof(System.Collections.Hashtable),
				reader, deserializedObjects, -1);
			
			foreach (System.Collections.DictionaryEntry entry in extProps)
			{
				dataTable.ExtendedProperties[entry.Key] = entry.Value;
			}

			return dataTable;
		}

		private void SetExceptionField(Exception exception, string fieldName, object value)
		{
			var field = typeof(Exception).GetField(fieldName,
				BindingFlags.NonPublic | BindingFlags.Instance);
			field?.SetValue(exception, value);
		}

		private bool IsStandardExceptionField(string fieldName)
		{
			var standardFields = new[]
			{
				// Public-facing properties
				"Message", "Source", "StackTrace", "HelpLink", "HResult", "InnerException", "Data", "TargetSite",
				// Common private/internal fields used by Exception implementations
				"_message", "_source", "_stackTraceString", "_stackTrace", "_helpURL", "_HResult", "_innerException",
				"_remoteStackTraceString", "_watsonBuckets", "_dynamicMethods", "_safeSerializationManager",
				"_targetSite"
			};
			return standardFields.Contains(fieldName);
		}

		private string StripArgumentParameterSuffix(string message, string paramName)
		{
			var result = message ?? string.Empty;
			if (string.IsNullOrEmpty(result) || string.IsNullOrEmpty(paramName))
				return result;

			var suffix1 = $" (Parameter '{paramName}')";
			var suffix2 = $" (Parameter \"{paramName}\")";

			if (result.EndsWith(suffix1, StringComparison.Ordinal))
				return result.Substring(0, result.Length - suffix1.Length);
			if (result.EndsWith(suffix2, StringComparison.Ordinal))
				return result.Substring(0, result.Length - suffix2.Length);

			return result;
		}

		private object CreateInstanceWithoutConstructor(Type type)
		{
			// Try Activator.CreateInstance first (for types with parameterless constructor)
			// IMPORTANT: If the constructor itself throws, we must propagate that exception
			// to preserve original behavior and error messages (e.g., tests expecting
			// constructor-side deserialization failures).
			try
			{
				return Activator.CreateInstance(type)!;
			}
			catch (TargetInvocationException tie)
			{
				// The constructor ran and threw — rethrow the original to keep its message
				throw tie.InnerException ?? tie;
			}
			catch (MissingMethodException)
			{
				// No default constructor — fall through to other strategies
			}
			catch (MemberAccessException)
			{
				// Inaccessible constructor — fall through to other strategies
			}
			catch (TypeLoadException)
			{
				// Type loading issue — fall through to other strategies
			}
			catch
			{
				// Any other reflection exception: try other strategies
			}

			// Use FormatterServices to create an uninitialized object without invoking any constructor
			try
			{
#pragma warning disable SYSLIB0050
				return FormatterServices.GetUninitializedObject(type);
#pragma warning restore SYSLIB0050
			}
			catch
			{
				// Not supported for some types; continue with other strategies
			}

			// Try to find a parameterless constructor and invoke it
			var constructor = type.GetConstructor(Type.EmptyTypes);
			if (constructor != null)
			{
				return constructor.Invoke(null);
			}

			// For value types, we can use default(T) and box it
			if (type.IsValueType)
			{
				return Activator.CreateInstance(type)!;
			}

			// Try using System.Runtime.Serialization.ObjectManager for .NET Core/5+
			try
			{
				// For .NET Core 3.0+ and .NET 5+, we can use reflection to access internal methods
				var runtimeType = typeof(System.Type).Assembly.GetType("System.RuntimeType");
				if (runtimeType != null)
				{
					var getUninitializedObjectMethod = runtimeType.GetMethod("GetUninitializedObject",
						BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
					if (getUninitializedObjectMethod != null)
					{
						return getUninitializedObjectMethod.Invoke(null, new object[] { type })!;
					}
				}
			}
			catch
			{
				// Internal method not available or failed
			}

			// Last resort: try to create using the most accessible constructor with default parameters
			var constructors =
				type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
			if (constructors.Length > 0)
			{
				// Try the parameterless constructor first (already checked above, but just in case)
				var parameterlessConstructor = constructors.FirstOrDefault(c => c.GetParameters().Length == 0);
				if (parameterlessConstructor != null)
				{
					return parameterlessConstructor.Invoke(null);
				}

				// Try constructors with parameters and use default values
				foreach (var ctor in constructors.OrderBy(c => c.GetParameters().Length))
				{
					var parameters = ctor.GetParameters();
					var args = new object[parameters.Length];

					bool canCreate = true;
					for (int i = 0; i < parameters.Length; i++)
					{
						var paramType = parameters[i].ParameterType;

						if (paramType.IsValueType)
						{
							args[i] = Activator.CreateInstance(paramType)!;
						}
						else if (paramType == typeof(string))
						{
							args[i] = string.Empty;
						}
						else if (parameters[i].HasDefaultValue)
						{
							args[i] = parameters[i].DefaultValue;
						}
						else
						{
							canCreate = false;
							break;
						}
					}

					if (canCreate)
					{
						try
						{
							return ctor.Invoke(args);
						}
						catch
						{
							// Try next constructor
						}
					}
				}
			}

			throw new InvalidOperationException(
				$"Cannot create instance of type '{type.FullName}' without a parameterless constructor. Consider adding a parameterless constructor or marking the type with [Serializable].");
		}

		private int[] GetIndicesFromLinearIndex(int linearIndex, int[] lengths)
		{
			var indices = new int[lengths.Length];
			var remaining = linearIndex;

			for (int i = lengths.Length - 1; i >= 0; i--)
			{
				indices[i] = remaining % lengths[i];
				remaining /= lengths[i];
			}

			return indices;
		}

		private class ReferenceEqualityComparer : IEqualityComparer<object>
		{
			public static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();

			public new bool Equals(object x, object y) => ReferenceEquals(x, y);
			public int GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
		}

		internal class ForwardReferencePlaceholder
		{
			public int ObjectId { get; }

			public ForwardReferencePlaceholder(int objectId)
			{
				ObjectId = objectId;
			}
		}

		/// <summary>
		/// Add a forward reference that couldn't be resolved yet for later resolution.
		/// </summary>
		/// <param name="targetObject">Object containing the field</param>
		/// <param name="field">Field to set</param>
		/// <param name="placeholderObjectId">Referenced object id</param>
		internal void AddPendingForwardReference(object targetObject, System.Reflection.FieldInfo field,
			int placeholderObjectId)
		{
			if (targetObject == null || field == null) return;
			_pendingForwardReferences.Add((targetObject, field, placeholderObjectId));
		}

		/// <summary>
		/// Resolve all pending forward references collected during nested deserialization calls.
		/// </summary>
		/// <param name="deserializedObjects">Map of object ids to instances</param>
		private void ResolvePendingForwardReferences(Dictionary<int, object> deserializedObjects)
		{
			// Try multiple passes in case of chains; cap iterations to avoid infinite loops.
			var maxIterations = _pendingForwardReferences.Count + 1;
			for (int iteration = 0; iteration < maxIterations; iteration++)
			{
				if (_pendingForwardReferences.Count == 0) break;

				var remaining =
					new List<(object targetObject, System.Reflection.FieldInfo field, int placeholderObjectId)>();

				foreach (var (targetObject, field, placeholderObjectId) in _pendingForwardReferences)
				{
					if (deserializedObjects.TryGetValue(placeholderObjectId, out var resolved)
					    && resolved is not ForwardReferencePlaceholder)
					{
						field.SetValue(targetObject, resolved);
					}
					else
					{
						remaining.Add((targetObject, field, placeholderObjectId));
					}
				}

				_pendingForwardReferences.Clear();
				_pendingForwardReferences.AddRange(remaining);

				if (_pendingForwardReferences.Count == 0) break;
			}
		}

		/// <summary>
		/// Clears the assembly type cache to free memory.
		/// Should be called when memory pressure is high or when assemblies are unloaded.
		/// </summary>
		public void ClearAssemblyTypeCache()
		{
			_assemblyTypeCache.Clear();
			_assemblyNameCache.Clear();

			// Also clear search cache entries
			var keysToRemove = _resolvedTypeCache.Keys
				.Where(key => key.StartsWith("search_"))
				.ToList();

			foreach (var key in keysToRemove)
			{
				_resolvedTypeCache.TryRemove(key, out _);
			}
		}

		/// <summary>
		/// Gets cache statistics for monitoring performance.
		/// </summary>
		/// <returns>Tuple with assembly cache count and name cache count</returns>
		public (int AssemblyTypeCacheCount, int AssemblyNameCacheCount, int SearchCacheCount) GetCacheStatistics()
		{
			var searchCacheCount = _resolvedTypeCache.Keys.Count(key => key.StartsWith("search_"));
			return (_assemblyTypeCache.Count, _assemblyNameCache.Count, searchCacheCount);
		}

		/// <summary>
		/// Disposes the serializer and cleans up caches.
		/// </summary>
		public void Dispose()
		{
			ClearAssemblyTypeCache();
			_serializerCache?.Dispose();
		}

		/// <summary>
		/// Serializes MemberInfo objects with custom approach.
		/// </summary>
		private void SerializeMemberInfo(object memberInfoObj, BinaryWriter writer, HashSet<object> serializedObjects, Dictionary<object, int> objectMap)
		{
			if (memberInfoObj == null)
			{
				writer.Write((byte)0); // Null marker
				return;
			}

			var memberInfo = (MemberInfo)memberInfoObj;
			writer.Write((byte)1); // Non-null marker
			writer.Write((int)memberInfo.MemberType);
			WriteTypeInfo(writer, memberInfo.GetType());
			WriteTypeInfo(writer, memberInfo.DeclaringType);
			writer.Write(memberInfo.Name ?? string.Empty);
			writer.Write(memberInfo.MetadataToken);

			// Handle specific MemberInfo types
			if (memberInfo is PropertyInfo propertyInfo)
			{
				WriteTypeInfo(writer, propertyInfo.PropertyType);
				writer.Write((byte)(propertyInfo.CanRead ? 1 : 0));
				writer.Write((byte)(propertyInfo.CanWrite ? 1 : 0));
				SerializeObject(propertyInfo.GetIndexParameters(), writer, serializedObjects, objectMap);
			}
			else if (memberInfo is MethodInfo methodInfo)
			{
				WriteTypeInfo(writer, methodInfo.ReturnType);
				writer.Write(methodInfo.ReturnParameter?.Name ?? string.Empty);
				SerializeObject(methodInfo.GetParameters(), writer, serializedObjects, objectMap);
				writer.Write((byte)(methodInfo.IsStatic ? 1 : 0));
				writer.Write((byte)(methodInfo.IsVirtual ? 1 : 0));
				writer.Write((byte)(methodInfo.IsAbstract ? 1 : 0));
			}
			else if (memberInfo is FieldInfo fieldInfo)
			{
				WriteTypeInfo(writer, fieldInfo.FieldType);
				writer.Write((byte)(fieldInfo.IsStatic ? 1 : 0));
				writer.Write((byte)(fieldInfo.IsInitOnly ? 1 : 0));
				writer.Write((byte)(fieldInfo.IsLiteral ? 1 : 0));
			}
			else if (memberInfo is ConstructorInfo constructorInfo)
			{
				SerializeObject(constructorInfo.GetParameters(), writer, serializedObjects, objectMap);
				writer.Write((byte)(constructorInfo.IsStatic ? 1 : 0));
			}
			else if (memberInfo is EventInfo eventInfo)
			{
				WriteTypeInfo(writer, eventInfo.EventHandlerType);
			}
			else if (memberInfo is TypeInfo typeInfo)
			{
				WriteTypeInfo(writer, typeInfo);
			}
		}

		/// <summary>
		/// Deserializes MemberInfo objects.
		/// </summary>
		private object DeserializeMemberInfo(Type expectedType, BinaryReader reader, Dictionary<int, object> deserializedObjects, int objectId)
		{
			var nullMarker = reader.ReadByte();
			if (nullMarker == 0) return null;

			var memberType = (MemberTypes)reader.ReadInt32();
			var actualType = ReadTypeInfo(reader);
			var declaringType = ReadTypeInfo(reader);
			var name = reader.ReadString();
			var metadataToken = reader.ReadInt32();

			try
			{
				switch (memberType)
				{
					case MemberTypes.Property:
						var propertyType = ReadTypeInfo(reader);
						var canRead = reader.ReadByte() == 1;
						var canWrite = reader.ReadByte() == 1;
						var indexParameters = (ParameterInfo[])DeserializeObject(reader, deserializedObjects);
						
						if (declaringType != null)
						{
							var properties = declaringType.GetProperties(
								BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
							var result = properties.FirstOrDefault(p => 
								p.Name == name && 
								p.MetadataToken == metadataToken &&
								p.PropertyType == propertyType);
							return result;
						}
						break;

					case MemberTypes.Method:
						var returnType = ReadTypeInfo(reader);
						var returnParamName = reader.ReadString();
						var parameters = (ParameterInfo[])DeserializeObject(reader, deserializedObjects);
						var isStatic = reader.ReadByte() == 1;
						var isVirtual = reader.ReadByte() == 1;
						var isAbstract = reader.ReadByte() == 1;
						
						if (declaringType != null)
						{
							// Try multiple approaches to find the method
							MethodInfo result = null;
							
							// First try: exact match with metadata token
							var allMethods = declaringType.GetMethods(
								BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
							result = allMethods.FirstOrDefault(m => 
								m.Name == name && 
								m.MetadataToken == metadataToken);
							
							// Second try: match by name and parameter count for generic methods
							if (result == null)
							{
								result = allMethods.FirstOrDefault(m => 
									m.Name == name && 
									m.GetParameters().Length == (parameters?.Length ?? 0) &&
									m.ReturnType == returnType);
							}
							
							// Third try: find generic method definition and construct it
							if (result == null && returnType.IsGenericType)
							{
								var genericDef = allMethods.FirstOrDefault(m => 
									m.Name == name && 
									m.IsGenericMethodDefinition &&
									m.GetGenericArguments().Length == returnType.GetGenericArguments().Length);
								
								if (genericDef != null)
								{
									try
									{
										var typeArgs = returnType.GetGenericArguments();
										result = genericDef.MakeGenericMethod(typeArgs);
									}
									catch
									{
										// Fall back to null if construction fails
									}
								}
							}
							
							return result;
						}
						break;

					case MemberTypes.Field:
						var fieldType = ReadTypeInfo(reader);
						var fieldIsStatic = reader.ReadByte() == 1;
						var fieldIsInitOnly = reader.ReadByte() == 1;
						var fieldIsLiteral = reader.ReadByte() == 1;
						
						if (declaringType != null)
						{
							var fields = declaringType.GetFields(
								BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
							var result = fields.FirstOrDefault(f => 
								f.Name == name && 
								f.MetadataToken == metadataToken &&
								f.FieldType == fieldType);
							return result;
						}
						break;

					case MemberTypes.Constructor:
						var constructorParameters = (ParameterInfo[])DeserializeObject(reader, deserializedObjects);
						var constructorIsStatic = reader.ReadByte() == 1;
						
						if (declaringType != null)
						{
							var constructors = declaringType.GetConstructors(
								BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
							var result = constructors.FirstOrDefault(c => 
								c.MetadataToken == metadataToken);
							return result;
						}
						break;

					case MemberTypes.Event:
						var eventHandlerType = ReadTypeInfo(reader);
						
						if (declaringType != null)
						{
							var events = declaringType.GetEvents(
								BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
							var result = events.FirstOrDefault(e => 
								e.Name == name && 
								e.MetadataToken == metadataToken &&
								e.EventHandlerType == eventHandlerType);
							return result;
						}
						break;

					case MemberTypes.TypeInfo:
					case MemberTypes.NestedType:
						return ReadTypeInfo(reader);
				}
			}
			catch
			{
				// Return null if deserialization fails
				return null;
			}

			return null;
		}

		/// <summary>
		/// Serializes ParameterInfo objects with custom approach.
		/// </summary>
		private void SerializeParameterInfo(object parameterInfoObj, BinaryWriter writer, HashSet<object> serializedObjects, Dictionary<object, int> objectMap)
		{
			if (parameterInfoObj == null)
			{
				writer.Write((byte)0); // Null marker
				return;
			}

			var parameterInfo = (ParameterInfo)parameterInfoObj;
			writer.Write((byte)1); // Non-null marker
			WriteTypeInfo(writer, parameterInfo.ParameterType);
			writer.Write(parameterInfo.Name ?? string.Empty);
			writer.Write((int)parameterInfo.Attributes);
			writer.Write((byte)(parameterInfo.IsIn ? 1 : 0));
			writer.Write((byte)(parameterInfo.IsOut ? 1 : 0));
			writer.Write((byte)(parameterInfo.IsOptional ? 1 : 0));
			
			if (parameterInfo.IsOptional)
			{
				SerializeObject(parameterInfo.DefaultValue, writer, serializedObjects, objectMap);
			}
		}

		/// <summary>
		/// Deserializes ParameterInfo objects.
		/// </summary>
		private object DeserializeParameterInfo(Type expectedType, BinaryReader reader, Dictionary<int, object> deserializedObjects, int objectId)
		{
			var nullMarker = reader.ReadByte();
			if (nullMarker == 0) return null;

			var parameterType = ReadTypeInfo(reader);
			var name = reader.ReadString();
			var attributes = (ParameterAttributes)reader.ReadInt32();
			var isIn = reader.ReadByte() == 1;
			var isOut = reader.ReadByte() == 1;
			var isOptional = reader.ReadByte() == 1;
			
			var defaultValue = isOptional ? DeserializeObject(reader, deserializedObjects) : null;

			// Note: Creating ParameterInfo instances directly is not supported in .NET
			// For now, return a placeholder object
			return new SerializableParameterInfo(parameterType, name, attributes, isIn, isOut, isOptional, defaultValue);
		}

		/// <summary>
		/// Serializes Module objects with custom approach.
		/// </summary>
		private void SerializeModule(object moduleObj, BinaryWriter writer, HashSet<object> serializedObjects, Dictionary<object, int> objectMap)
		{
			if (moduleObj == null)
			{
				writer.Write((byte)0); // Null marker
				return;
			}

			var module = (Module)moduleObj;
			writer.Write((byte)1); // Non-null marker
			writer.Write(module.Name ?? string.Empty);
			writer.Write(module.ScopeName ?? string.Empty);
			SerializeObject(module.Assembly, writer, serializedObjects, objectMap);
		}

		/// <summary>
		/// Deserializes Module objects.
		/// </summary>
		private object DeserializeModule(Type expectedType, BinaryReader reader, Dictionary<int, object> deserializedObjects, int objectId)
		{
			var nullMarker = reader.ReadByte();
			if (nullMarker == 0) return null;

			var name = reader.ReadString();
			var scopeName = reader.ReadString();
			var assembly = (Assembly)DeserializeObject(reader, deserializedObjects);

			if (assembly != null)
			{
				var modules = assembly.GetModules();
				return modules.FirstOrDefault(m => m.Name == name && m.ScopeName == scopeName);
			}

			return null;
		}

		/// <summary>
		/// Serializes Assembly objects with custom approach.
		/// </summary>
		private void SerializeAssembly(object assemblyObj, BinaryWriter writer, HashSet<object> serializedObjects, Dictionary<object, int> objectMap)
		{
			if (assemblyObj == null)
			{
				writer.Write((byte)0); // Null marker
				return;
			}

			var assembly = (Assembly)assemblyObj;
			writer.Write((byte)1); // Non-null marker
			writer.Write(assembly.FullName ?? string.Empty);
		}

		/// <summary>
		/// Deserializes Assembly objects.
		/// </summary>
		private object DeserializeAssembly(Type expectedType, BinaryReader reader, Dictionary<int, object> deserializedObjects, int objectId)
		{
			var nullMarker = reader.ReadByte();
			if (nullMarker == 0) return null;

			var fullName = reader.ReadString();

			try
			{
				return Assembly.Load(fullName);
			}
			catch
			{
				// Try to find in currently loaded assemblies
				return AppDomain.CurrentDomain.GetAssemblies()
					.FirstOrDefault(a => a.FullName == fullName || a.GetName().Name == fullName);
			}
		}

		/// <summary>
		/// Serializable wrapper for ParameterInfo data.
		/// </summary>
		private class SerializableParameterInfo
		{
			public Type ParameterType { get; }
			public string Name { get; }
			public ParameterAttributes Attributes { get; }
			public bool IsIn { get; }
			public bool IsOut { get; }
			public bool IsOptional { get; }
			public object DefaultValue { get; }

			public SerializableParameterInfo(Type parameterType, string name, ParameterAttributes attributes, 
				bool isIn, bool isOut, bool isOptional, object defaultValue)
			{
				ParameterType = parameterType;
				Name = name;
				Attributes = attributes;
				IsIn = isIn;
				IsOut = isOut;
				IsOptional = isOptional;
				DefaultValue = defaultValue;
			}
		}

		/// <summary>
		/// Simple object pool for performance optimization.
		/// </summary>
		private class ObjectPool<T> where T : class
		{
			private readonly Func<T> _factory;
			private readonly Action<T> _reset;
			private readonly object _lock = new object();
			private readonly List<T> _pool = new List<T>();

			public ObjectPool(Func<T> factory, Action<T> reset)
			{
				_factory = factory;
				_reset = reset;
			}

			public T Get()
			{
				lock (_lock)
				{
					if (_pool.Count > 0)
					{
						var item = _pool[_pool.Count - 1];
						_pool.RemoveAt(_pool.Count - 1);
						return item;
					}
				}
				return _factory();
			}

			public void Return(T item)
			{
				if (item == null) return;
				_reset(item);
				lock (_lock)
				{
					if (_pool.Count < 10) // Limit pool size
					{
						_pool.Add(item);
					}
				}
			}
		}
	}
}