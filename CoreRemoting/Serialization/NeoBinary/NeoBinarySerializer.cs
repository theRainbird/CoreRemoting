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
	public partial class NeoBinarySerializer
	{
		#region Declarations

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
		private static readonly ConcurrentDictionary<string, Type> _commonTypes = new()
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
		private readonly ObjectPool<HashSet<object>> _hashSetPool =
			new ObjectPool<HashSet<object>>(() => new HashSet<object>(ReferenceEqualityComparer.Instance),
				set => set.Clear());

		private readonly ObjectPool<Dictionary<object, int>> _objectMapPool =
			new ObjectPool<Dictionary<object, int>>(
				() => new Dictionary<object, int>(ReferenceEqualityComparer.Instance), dict => dict.Clear());

		#endregion

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
			// Additional validation: This should NEVER be called for complex types or arrays
			if ((type.IsClass && !type.IsEnum && type != typeof(string)) || type.IsArray)
			{
				throw new InvalidOperationException(
					$"DeserializePrimitive called for complex type: {type.FullName}. This indicates a serious bug in type resolution or serialization.");
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
			// Arrays are never simple types
			if (type.IsArray)
				return false;
				
			return type.IsPrimitive || type == typeof(string) || type == typeof(decimal) || type == typeof(UIntPtr) ||
			       type == typeof(IntPtr) || type == typeof(DateTime);
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
		/// Disposes the serializer and cleans up caches.
		/// </summary>
		public void Dispose()
		{
			ClearAssemblyTypeCache();
			_serializerCache?.Dispose();
		}
	}
}