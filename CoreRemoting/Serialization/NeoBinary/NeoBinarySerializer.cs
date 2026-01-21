using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;

namespace CoreRemoting.Serialization.NeoBinary;

/// <summary>
/// Modern binary serializer that replaces BinaryFormatter with enhanced security and performance.
/// </summary>
public partial class NeoBinarySerializer
{
	#region Declarations

	// Pending forward references collected during nested deserializations
	// that could not be resolved immediately (e.g., child -> parent back-references).
	// They will be resolved after the full object graph has been materialized.
	private readonly List<(object targetObject, FieldInfo field, int placeholderObjectId)>
		_pendingForwardReferences = [];

	private const string MAGIC_NAME = "NEOB";
	private const ushort CURRENT_VERSION = 1;

	private static readonly string COREMOTING_VERSION =
		typeof(NeoBinarySerializer).Assembly.GetName().Version?.ToString() ?? "Unknown";

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

	// Thread synchronization objects for safe concurrent access
	private readonly object _pendingForwardReferencesLock = new();

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

	// --- Thread-safe serialization context for per-operation isolation ---

	// --- Thread-local context storage for per-operation isolation ---
	[ThreadStatic] private static TypeReferenceContext _currentContext;

	// --- NEW: Assembly type index cache (shared across all instances) ---
	private static readonly ConcurrentDictionary<Assembly, Dictionary<string, Type>> _assemblyTypeIndex = new();

	// --- NEW: Validated types cache ---
	private readonly ConcurrentDictionary<Type, bool> _validatedTypes = new();

	// --- NEW: Object pools for performance optimization ---
	private readonly ObjectPool<HashSet<object>> _hashSetPool =
		new(() => new HashSet<object>(ReferenceEqualityComparer.Instance),
			set => set.Clear());

	private readonly ObjectPool<Dictionary<object, int>> _objectMapPool =
		new(
			() => new Dictionary<object, int>(ReferenceEqualityComparer.Instance), dict => dict.Clear());

	#endregion

	/// <summary>
	/// Gets or sets the serializer configuration.
	/// </summary>
	public NeoBinarySerializerConfig Config { get; set; } = new();

	/// <summary>
	/// Gets or sets the type validator for security.
	/// </summary>
	public NeoBinaryTypeValidator TypeValidator { get; set; } = new();

	/// <summary>
	/// Serializes an object to the specified stream.
	/// </summary>
	/// <param name="graph">Object to serialize</param>
	/// <param name="serializationStream">Stream to write to</param>
	public void Serialize(object graph, Stream serializationStream)
	{
		if (serializationStream == null)
			throw new ArgumentNullException(nameof(serializationStream));

		using var writer = new BinaryWriter(serializationStream, Encoding.UTF8, true);

		// Initialize type-ref tables for this session if enabled
		if (Config.UseTypeReferences)
		{
			_currentContext = new TypeReferenceContext
			{
				TypeRefActive = true
			};
		}
		else
		{
			_currentContext = null;
		}

		// Fast path for primitive types - avoid overhead of reference tracking
		if (graph != null && IsSimpleType(graph.GetType()))
		{
			WriteHeader(writer);
			writer.Write((byte)3); // Simple object marker
			WriteTypeInfo(writer, graph.GetType());
			SerializePrimitive(graph, writer);
			writer.Flush();
			_currentContext = null;
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
				SerializeObject(graph, writer, serializedObjects, objectMap);
			else
				// Write null marker
				writer.Write((byte)0);

			writer.Flush();
		}
		finally
		{
			_hashSetPool.Return(serializedObjects);
			_objectMapPool.Return(objectMap);
			_currentContext = null;
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

		using var reader = new BinaryReader(serializationStream, Encoding.UTF8, true);

		// Initial stream validation
		ValidateStreamState(reader, "Deserialize start");

		// Initialize type-ref tables for this session if enabled
		if (Config.UseTypeReferences)
		{
			_currentContext = new TypeReferenceContext
			{
				TypeRefActive = true
			};
		}
		else
		{
			_currentContext = null;
		}

		// Read and validate header
		ReadHeader(reader);
		ValidateStreamState(reader, "After header read");

		// Peek at the first byte to determine if it's a simple type
		var firstByte = reader.ReadByte();
		if (firstByte == 0) return null;

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

		_currentContext = null;
		return result;
	}

	private void WriteHeader(BinaryWriter writer)
	{
		writer.Write(Encoding.ASCII.GetBytes(MAGIC_NAME));
		writer.Write(CURRENT_VERSION);

		// Write CoreRemoting version string for debugging version mismatches
		writer.Write(COREMOTING_VERSION);

		// Write flags
		ushort flags = 0;
		if (Config.IncludeAssemblyVersions) flags |= 0x01;
		if (Config.UseTypeReferences) flags |= 0x02;
		writer.Write(flags);
	}

	private static void ReadHeader(BinaryReader reader)
	{
		var startPosition = reader.BaseStream.CanSeek ? reader.BaseStream.Position : -1;

		var magicBytes = reader.ReadBytes(4);
		// Compare directly against ASCII constants: 'N','E','O','B'
		if (magicBytes.Length != 4 || magicBytes[0] != (byte)'N' || magicBytes[1] != (byte)'E' ||
		    magicBytes[2] != (byte)'O' || magicBytes[3] != (byte)'B')
		{
			var error = "Invalid magic number: expected NEOB";
			if (startPosition >= 0)
				error += $" at stream position {startPosition}";
			if (magicBytes.Length > 0)
				error += $", got: {BitConverter.ToString(magicBytes)}";
			throw new InvalidOperationException(error);
		}

		var version = reader.ReadUInt16();
		if (version > CURRENT_VERSION)
		{
			var error = $"Unsupported version: {version} (current: {CURRENT_VERSION})";
			if (startPosition >= 0)
				error += $" at stream position {reader.BaseStream.Position - 2}";
			throw new InvalidOperationException(error);
		}

		// Read CoreRemoting version for compatibility checking
		var remoteVersion = reader.ReadString();
		var localVersion = COREMOTING_VERSION;
		reader.ReadUInt16(); // Read flags

		// Enhanced version negotiation
		var versionMismatch = !string.IsNullOrEmpty(remoteVersion) && remoteVersion != localVersion;
		if (!versionMismatch) 
			return;
		
		// Parse versions to compare major/minor versions
		if (!Version.TryParse(localVersion, out var localVer) ||
		    !Version.TryParse(remoteVersion, out var remoteVer)) 
			return;
			
		// Check for major version incompatibility
		if (localVer.Major != remoteVer.Major)
			throw new InvalidOperationException(
				$"Major version mismatch detected. Local: {localVersion}, Remote: {remoteVersion}. " +
				$"This indicates incompatible CoreRemoting versions.");
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
			SerializeEnum(obj, writer);
		else if (type.IsArray)
			SerializeArray((Array)obj, writer, serializedObjects, objectMap);
		else if (typeof(IList).IsAssignableFrom(type))
			SerializeList((IList)obj, writer, serializedObjects, objectMap);
		else if (obj is ExpandoObject expando)
			SerializeExpandoObject(expando, writer, serializedObjects, objectMap);
		else if (typeof(IDictionary).IsAssignableFrom(type))
			SerializeDictionary((IDictionary)obj, writer, serializedObjects, objectMap);
		else if (typeof(DataSet).IsAssignableFrom(type))
			SerializeDataSet((DataSet)obj, writer, serializedObjects, objectMap);
		else if (typeof(DataTable).IsAssignableFrom(type))
			SerializeDataTable((DataTable)obj, writer, serializedObjects, objectMap);
		else if (typeof(Exception).IsAssignableFrom(type))
			SerializeException((Exception)obj, writer, serializedObjects, objectMap);
		else if (typeof(Expression).IsAssignableFrom(type))
			SerializeExpression((Expression)obj, writer, serializedObjects, objectMap);
		else if (typeof(Type).IsAssignableFrom(type))
			// Serialize Type objects specially to avoid MemberInfo handling
			WriteTypeInfo(writer, (Type)obj);
		else if (typeof(MemberInfo).IsAssignableFrom(type))
			// Serialize MemberInfo with custom approach
			SerializeMemberInfo(obj, writer, serializedObjects, objectMap);
		else if (typeof(ParameterInfo).IsAssignableFrom(type))
			// Serialize ParameterInfo with custom approach
			SerializeParameterInfo(obj, writer, serializedObjects, objectMap);
		else if (typeof(Module).IsAssignableFrom(type))
			// Serialize Module with custom approach
			SerializeModule(obj, writer, serializedObjects, objectMap);
		else if (typeof(Assembly).IsAssignableFrom(type))
			// Serialize Assembly with custom approach
			SerializeAssembly(obj, writer, serializedObjects, objectMap);
		else
			// Serialize any complex object regardless of [Serializable] attribute
			SerializeComplexObject(obj, writer, serializedObjects, objectMap);
	}

	/// <summary>
	/// Validates stream integrity by checking length prefixes and markers.
	/// </summary>
	/// <param name="reader">The binary reader to validate</param>
	/// <param name="context">Context for error reporting</param>
	private static void ValidateStreamIntegrity(BinaryReader reader, string context)
	{
		if (!reader.BaseStream.CanSeek)
			return; // Cannot validate non-seekable streams

		var currentPosition = reader.BaseStream.Position;
		var remainingBytes = reader.BaseStream.Length - currentPosition;

		// Basic sanity checks
		if (remainingBytes < 0)
			throw new InvalidOperationException(
				$"Stream position {currentPosition} exceeds stream length {reader.BaseStream.Length} in {context}");

		// Check for sufficient bytes for marker validation
		if (remainingBytes > 0 && remainingBytes < 1)
			throw new InvalidOperationException(
				$"Insufficient bytes remaining ({remainingBytes}) for marker reading in {context}");
	}

	/// <summary>
	/// Creates a stream checkpoint for rollback operations.
	/// </summary>
	/// <param name="reader">The binary reader</param>
	/// <param name="context">Context description for debugging</param>
	/// <returns>Stream position for rollback</returns>
	private long CreateStreamCheckpoint(BinaryReader reader, string context)
	{
		if (!reader.BaseStream.CanSeek)
			return -1; // Cannot checkpoint non-seekable streams

		var position = reader.BaseStream.Position;

		return position;
	}

	/// <summary>
	/// Analyzes stream state for debugging and error reporting.
	/// </summary>
	/// <param name="reader">The binary reader to analyze</param>
	/// <returns>Detailed stream state information</returns>
	private string GetStreamDiagnosis(BinaryReader reader)
	{
		if (!reader.BaseStream.CanSeek)
			return "Non-seekable stream - cannot diagnose";

		var position = reader.BaseStream.Position;
		var length = reader.BaseStream.Length;
		var remaining = length - position;

		var diagnosis = $"Position: {position}, Length: {length}, Remaining: {remaining}";

		// Try to peek ahead to see what's next
		if (remaining > 0)
			try
			{
				var nextByte = reader.ReadByte();
				if (nextByte >= 0)
				{
					var asChar = nextByte is >= 32 and <= 126 ? (char)nextByte : '?';
					diagnosis += $", Next: 0x{nextByte:X2} ('{asChar}')";

					// Roll back the byte we read
					if (reader.BaseStream.CanSeek)
						reader.BaseStream.Position = position;
				}
			}
			catch
			{
				diagnosis += ", Next: <unreadable>";
			}
		else
			diagnosis += ", Next: <EOS>";

		// Check for common corruption patterns
		if (remaining <= 4) 
			return diagnosis;
		
		try
		{
			var bytesAhead = new byte[4];
			var actuallyRead = reader.BaseStream.Read(bytesAhead, 0, 4);
			if (actuallyRead > 0)
			{
				var hexString = BitConverter.ToString(bytesAhead, 0, actuallyRead).Replace("-", " ");
				diagnosis += $", Ahead: {hexString}";
			}

			// Roll back
			if (reader.BaseStream.CanSeek)
				reader.BaseStream.Position = position;
		}
		catch
		{
			diagnosis += ", Ahead: <unreadable>";
		}

		return diagnosis;
	}

	private object DeserializeObject(BinaryReader reader, Dictionary<int, object> deserializedObjects)
	{
		ValidateStreamState(reader, "DeserializeObject start");
		ValidateStreamIntegrity(reader, "DeserializeObject start");
		var streamPosition = reader.BaseStream.CanSeek ? reader.BaseStream.Position : -1;
		var checkpoint = CreateStreamCheckpoint(reader, "Read marker");
		var marker = reader.ReadByte();

		// Enhanced marker validation
		if (!IsValidMarker(marker))
		{
			// Try to interpret as potential type name or other data
			var potentialString = TryReadStringFromMarker(reader, marker);
			var streamDiagnosis = GetStreamDiagnosis(reader);

			throw new InvalidOperationException(
				$"Invalid marker: {marker} (0x{marker:X2}) at position {streamPosition}. " +
				$"This appears to be raw data '{potentialString}' where a marker was expected. " +
				$"Common causes: version mismatch, protocol corruption, or mixed serialization formats. " +
				$"Stream diagnosis: {streamDiagnosis}");
		}

		switch (marker)
		{
			// Null marker
			case 0:
				return null;
			// Reference marker
			case 2:
			{
				var objectId = reader.ReadInt32();

				// If object is not yet deserialized, create a forward reference
				if (!deserializedObjects.ContainsKey(objectId))
					// Create a forward reference placeholder
					deserializedObjects[objectId] = new ForwardReferencePlaceholder(objectId);

				return deserializedObjects[objectId];
			}
			// Simple object marker
			case 3:
			{
				ValidateStreamState(reader, "Simple object marker type info");
				var type = ReadTypeInfo(reader);

				// Defensive: apply same type checking order as object marker
				if (type.IsArray)
					return DeserializeArray(type, reader, deserializedObjects, -1);
				if (typeof(IList).IsAssignableFrom(type))
					return DeserializeList(type, reader, deserializedObjects, -1);
				if (typeof(IDictionary).IsAssignableFrom(type))
					return DeserializeDictionary(type, reader, deserializedObjects, -1);
				if (type == typeof(ExpandoObject))
					return DeserializeDictionary(type, reader, deserializedObjects, -1);
				if (type.IsEnum)
					return DeserializeEnum(type, reader);
				if (IsSimpleType(type))
					return DeserializePrimitive(type, reader);
				return DeserializeComplexObject(type, reader, deserializedObjects, -1);
			}
			// Object marker
			case 1:
			{
				ValidateStreamState(reader, "Object marker ID and type info");
				var objectId = reader.ReadInt32();
				var type = ReadTypeInfo(reader);

				object obj;

				// Arrays should be checked first - they have highest priority
				if (type.IsArray)
					obj = DeserializeArray(type, reader, deserializedObjects, objectId);
				else if (IsSimpleType(type))
					obj = DeserializePrimitive(type, reader);
				else if (type.IsEnum)
					obj = DeserializeEnum(type, reader);
				else if (typeof(IList).IsAssignableFrom(type))
					obj = DeserializeList(type, reader, deserializedObjects, objectId);
				else if (type == typeof(ExpandoObject))
					obj = DeserializeDictionary(type, reader, deserializedObjects, objectId);
				else if (typeof(IDictionary).IsAssignableFrom(type))
					obj = DeserializeDictionary(type, reader, deserializedObjects, objectId);
				else if (typeof(DataSet).IsAssignableFrom(type))
					obj = DeserializeDataSet(type, reader, deserializedObjects, objectId);
				else if (typeof(DataTable).IsAssignableFrom(type))
					obj = DeserializeDataTable(type, reader, deserializedObjects, objectId);
				else if (typeof(Exception).IsAssignableFrom(type))
					obj = DeserializeException(type, reader, deserializedObjects, objectId);
				else if (typeof(Expression).IsAssignableFrom(type))
					obj = DeserializeExpression(reader, deserializedObjects);
				else if (typeof(MemberInfo).IsAssignableFrom(type))
					obj = DeserializeMemberInfo(type, reader, deserializedObjects, objectId);
				else if (typeof(ParameterInfo).IsAssignableFrom(type))
					obj = DeserializeParameterInfo(type, reader, deserializedObjects, objectId);
				else if (typeof(Module).IsAssignableFrom(type))
					obj = DeserializeModule(type, reader, deserializedObjects, objectId);
				else if (typeof(Assembly).IsAssignableFrom(type))
					obj = DeserializeAssembly(type, reader, deserializedObjects, objectId);
				else
					obj = DeserializeComplexObject(type, reader, deserializedObjects, objectId);

				RegisterObjectWithReverseMapping(deserializedObjects, objectId, obj);
				return obj;
			}
		}

		// For complex reflection object graphs, some markers might not be handled
		// This is a graceful fallback to prevent test failures, but with enhanced debugging
		var errorMessage = $"Invalid marker: {marker} (0x{marker:X2})";

		if (streamPosition >= 0) errorMessage += $"\nDeserializeObject called at stream position: {streamPosition - 1}";

		// Add hex dump for debugging corruption issues
		if (!reader.BaseStream.CanSeek) 
			throw new InvalidOperationException(errorMessage);
		
		var originalPosition = reader.BaseStream.Position;
		
		try
		{
			// Try to read context around the invalid marker
			var contextStart = Math.Max(0, originalPosition - 16);
			var contextEnd = Math.Min(reader.BaseStream.Length, originalPosition + 16);
			var contextLength = (int)(contextEnd - contextStart);

			reader.BaseStream.Position = contextStart;
			var contextBytes = reader.ReadBytes(contextLength);

			errorMessage += $"\nStream context (hex dump):\n{FormatHexDump(contextBytes, contextStart)}";
			errorMessage += $"\nMarker position: {originalPosition - 1} (marked with >> <<)";

			// Reset position
			reader.BaseStream.Position = originalPosition;
		}
		catch (Exception ex)
		{
			errorMessage += $"\nFailed to capture stream context: {ex.Message}";
		}

		throw new InvalidOperationException(errorMessage);
	}

	private void WriteTypeInfo(BinaryWriter writer, Type type)
	{
		if (type == null)
		{
			// Special handling for null type to keep stream alignment consistent
			if (_currentContext?.TypeRefActive == true)
			{
				// Emit a "new type" entry with empty strings and a proper ID,
				// so the reader consumes the same shape (marker + 3 strings + int)
				var newIdForNull = _currentContext.TypeTable.Count;

				writer.Write((byte)0); // new type definition marker
				writer.Write(string.Empty); // type name
				writer.Write(string.Empty); // assembly name
				writer.Write(string.Empty); // version
				writer.Write(newIdForNull); // ID to keep table/index alignment

				// Keep table/index alignment; store typeof(object) as a benign placeholder
				// and map the empty key to this ID to avoid duplicates
				if (_currentContext.TypeTable.Count == newIdForNull)
					_currentContext.TypeTable.Add(typeof(object));
				var emptyKey = "||"; // type|asm|ver all empty
				if (!_currentContext.TypeKeyToId.ContainsKey(emptyKey))
					_currentContext.TypeKeyToId[emptyKey] = newIdForNull;
			}
			else
			{
				// Legacy path wrote three empty strings historically
				writer.Write(string.Empty); // Empty type name for null
				writer.Write(string.Empty); // Empty assembly name for null
				writer.Write(string.Empty); // Empty version for null
			}

			return;
		}

		var assemblyName = type.Assembly.GetName();

		// Validate type information before writing
		if (string.IsNullOrEmpty(type.FullName) && string.IsNullOrEmpty(type.Name))
			throw new InvalidOperationException(
				$"Cannot serialize type '{type}' - both FullName and Name are null or empty");

		string typeName;

		if (_currentContext?.TypeRefActive == true)
		{
			// With type references enabled, write a compact reference entry
			// Build type key based on config
			typeName = Config.IncludeAssemblyVersions
				? type.FullName ?? type.Name
				: BuildAssemblyNeutralTypeName(type);
			var asmName = assemblyName.Name ?? string.Empty;
			var versionString = Config.IncludeAssemblyVersions
				? assemblyName.Version?.ToString() ?? string.Empty
				: string.Empty;

			// Validate before creating key
			if (string.IsNullOrEmpty(typeName))
				throw new InvalidOperationException(
					$"Cannot serialize type '{type}' - resolved type name is empty");

			var key = typeName + "|" + asmName + "|" + versionString;

			if (_currentContext.TypeKeyToId.TryGetValue(key, out var existingId))
			{
				// Write reference marker and ID
				writer.Write((byte)1);
				writer.Write(existingId);
				return;
			}

			// New type definition
			var newId = _currentContext.TypeTable.Count;
			_currentContext.TypeKeyToId[key] = newId;
			_currentContext.TypeTable.Add(type);

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
			typeName = type.FullName ?? type.Name;
		else
			typeName = BuildAssemblyNeutralTypeName(type);

		// Validate before writing
		if (string.IsNullOrEmpty(typeName))
			throw new InvalidOperationException($"Cannot serialize type '{type}' - resolved type name is empty");

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
		if (_currentContext?.TypeRefActive == true)
		{
			var kind = reader.ReadByte();
			if (kind == 1)
			{
				var id = reader.ReadInt32();
				if (id < 0 || id >= _currentContext.TypeTable.Count)
					throw new InvalidOperationException(
						$"Invalid type reference ID: {id} (type table size: {_currentContext.TypeTable.Count})");

				var t = _currentContext.TypeTable[id];
				return t;
			}

			// If 'kind' is not a valid type-ref marker (0=new,1=ref), we may be reading a legacy triple
			// inside a type-ref session (caused by mixed producer versions). Recover by falling back
			// to legacy triple parsing and also registering type into type table.
			if (kind != 0)
				if (reader.BaseStream.CanSeek)
				{
					// Step back and parse legacy triple
					reader.BaseStream.Seek(-1, SeekOrigin.Current);
					var typeNameLegacy = reader.ReadString();
					var assemblyNameLegacy = reader.ReadString();
					var assemblyVersionLegacy = reader.ReadString();
					var tLegacy = ResolveTypeCore(typeNameLegacy, assemblyNameLegacy, assemblyVersionLegacy);

					// Register in type table with next running id
					var assignedId = _currentContext.TypeTable.Count;
					_currentContext.TypeTable.Add(tLegacy);
					var legacyKey = $"{typeNameLegacy}|{assemblyNameLegacy}|{assemblyVersionLegacy}";
					if (!_currentContext.TypeKeyToId.ContainsKey(legacyKey))
						_currentContext.TypeKeyToId[legacyKey] = assignedId;

					// Some producers (mixed versions) may append an Int32 ID after legacy triple
					// Try to consume it if it matches the assigned id to keep stream alignment
					if (!reader.BaseStream.CanSeek) 
						return tLegacy;
					var posBeforeId = reader.BaseStream.Position;
					try
					{
						if (reader.BaseStream.Length - posBeforeId >= 4)
						{
							var idCandidate = reader.ReadInt32();
							if (idCandidate != assignedId)
								// Not the expected id; rewind so caller can read whatever follows
								reader.BaseStream.Seek(-4, SeekOrigin.Current);
						}
					}
					catch
					{
						// On any issue, rewind to safe position
						reader.BaseStream.Position = posBeforeId;
					}

					return tLegacy;
				}

			// New type definition
			var typeNameNew = reader.ReadString();
			var assemblyNameNew = reader.ReadString();
			var assemblyVersionNew = reader.ReadString();
			var newId = reader.ReadInt32();

			// Handle null-type placeholder (all fields empty) without invoking resolver
			Type tResolved;
			if (string.IsNullOrEmpty(typeNameNew) && string.IsNullOrEmpty(assemblyNameNew) &&
			    string.IsNullOrEmpty(assemblyVersionNew))
				tResolved = typeof(object);
			else
				tResolved = ResolveTypeCore(typeNameNew, assemblyNameNew, assemblyVersionNew);

			// ensure table size/order correctness
			if (newId != _currentContext.TypeTable.Count)
				// fill any gaps (shouldn't happen) to maintain index safety
				while (_currentContext.TypeTable.Count < newId)
					_currentContext.TypeTable.Add(typeof(object));

			_currentContext.TypeTable.Add(tResolved);
			var keyNew = $"{typeNameNew}|{assemblyNameNew}|{assemblyVersionNew}";
			_currentContext.TypeKeyToId[keyNew] = newId;
			return tResolved;
		}

		// Legacy path
		var typeName = reader.ReadString();
		var assemblyName = reader.ReadString();
		var assemblyVersion = reader.ReadString();

		// Legacy null-type placeholder handling
		if (string.IsNullOrEmpty(typeName) && string.IsNullOrEmpty(assemblyName) &&
		    string.IsNullOrEmpty(assemblyVersion))
			return typeof(object);

		return ResolveTypeCore(typeName, assemblyName, assemblyVersion);
	}

	private Type ResolveTypeCore(string typeName, string assemblyName, string assemblyVersion)
	{
		// Normalize potentially corrupted/legacy type triples before strict validation
		// Case 1: Whole assembly-qualified name accidentally written into assemblyName
		// Example from logs: TypeName='', AssemblyName='SoloCRM.Shared.MailAccount', Version='SoloCRM.Shared'
		if (string.IsNullOrEmpty(typeName) && !string.IsNullOrEmpty(assemblyName))
		{
			// Try direct resolve from assemblyName as a type name first
			var tryDirect = SafeGetType(assemblyName);
			if (tryDirect != null)
			{
				typeName = assemblyName;
				// If version field actually contains the assembly simple name, reuse it
				if (!string.IsNullOrEmpty(assemblyVersion) && assemblyVersion.IndexOf('.') == -1)
				{
					assemblyName = assemblyVersion;
					assemblyVersion = string.Empty;
				}
				else
				{
					// otherwise clear assemblyName to let resolution search loaded assemblies
					assemblyName = string.Empty;
				}
			}
			else if (LooksLikeFullTypeName(assemblyName))
			{
				// Heuristic: assemblyName is actually the type; version contains assembly simple name
				typeName = assemblyName;
				assemblyName = string.IsNullOrEmpty(assemblyVersion)
					? assemblyName.Split('.').FirstOrDefault() ?? string.Empty
					: assemblyVersion;
				assemblyVersion = string.Empty;
			}
		}

		// Validate inputs and detect corrupted data (after normalization attempts)
		if (string.IsNullOrEmpty(typeName))
			throw new InvalidOperationException(
				$"Invalid type information received: TypeName='{typeName}', AssemblyName='{assemblyName}', Version='{assemblyVersion}'. " +
				"This indicates corrupted serialization data or incompatible serializer versions.");

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
					var map = _assemblyTypeIndex.GetOrAdd(assembly,
						(Func<Assembly, Dictionary<string, Type>>)BuildTypeIndexForAssembly);
					if (!map.TryGetValue(typeName, out type))
						// Try short name as a weak fallback
						type = map.Values.FirstOrDefault(t => t.Name == typeName);
				}

				type ??= Type.GetType(typeName);
			}
		}

		type ??= ResolveAssemblyNeutralType(typeName);

		// Final fallback: handle simple array types (highest priority fallback)
		if (type == null && !string.IsNullOrEmpty(typeName) && typeName.EndsWith("[]", StringComparison.Ordinal))
		{
			var elementTypeName = typeName.Substring(0, typeName.Length - 2);
			var elementType = ResolveTypeCore(elementTypeName, assemblyName, assemblyVersion);
			if (elementType != null) type = elementType.MakeArrayType();
		}

		if (type == null)
			throw new TypeLoadException(
				$"Cannot load type: '{typeName}', Assembly: '{assemblyName}', Version: '{assemblyVersion}'");

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
	/// Safely tries to resolve a type by name using Type.GetType without throwing.
	/// </summary>
	/// <param name="name">Type name (may be assembly-qualified)</param>
	/// <returns>Type or null</returns>
	private static Type SafeGetType(string name)
	{
		try
		{
			return string.IsNullOrEmpty(name) ? null : Type.GetType(name, false);
		}
		catch
		{
			return null;
		}
	}

	/// <summary>
	/// Heuristic check if a string looks like a fully qualified type name.
	/// </summary>
	/// <param name="candidate">Candidate string</param>
	/// <returns>True if it looks like a type name</returns>
	private static bool LooksLikeFullTypeName(string candidate)
	{
		if (string.IsNullOrEmpty(candidate)) 
			return false;
		// must contain a namespace separator and no whitespace; should not be only assembly simple name
		if (!candidate.Contains('.')) 
			return false;
		
		return candidate.IndexOf(' ') < 0;
		// avoid obvious assembly version patterns like x.y.z
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
		if (obj is ForwardReferencePlaceholder or null) 
			return;
		
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

	private static List<string> ParseGenericArgumentNames(string argsPart)
	{
		// argsPart starts with [[ and ends with ]] possibly; we extract inside and split at top-level '],['
		if (argsPart.StartsWith("[[") && argsPart.EndsWith("]]")) argsPart = argsPart.Substring(2, argsPart.Length - 4);

		var result = new List<string>();
		var sb = new StringBuilder();
		var depth = 0; // depth of bracket nesting considering double brackets

		for (var i = 0; i < argsPart.Length; i++)
		{
			var c = argsPart[i];

			switch (c)
			{
				case '[':
					depth++;
					sb.Append(c);
					continue;
				case ']':
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

		if (sb.Length > 0) result.Add(TrimBrackets(sb.ToString()));

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
			// This indicates a serious logic error in the serialization pipeline
			// Create detailed error with stream position for debugging
			var streamPosition = reader.BaseStream.CanSeek ? reader.BaseStream.Position : -1;
			throw new InvalidOperationException(
				$"DeserializePrimitive called for non-simple type: {type.FullName} at stream position {streamPosition}. " +
				"This indicates a serialization logic error.");
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

		var errorPosition = reader.BaseStream.CanSeek ? reader.BaseStream.Position : -1;
		var errorMessage = $"Unsupported primitive type: {type}";
		if (errorPosition >= 0)
			errorMessage += $" at stream position {errorPosition}";
		throw new InvalidOperationException(errorMessage);
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
		// Arrays and collections are never simple types
		if (type.IsArray || typeof(ICollection).IsAssignableFrom(type)) return false;

		// Only true primitive types and well-known value types are simple
		var isSimple = type.IsPrimitive || type == typeof(string) || type == typeof(decimal) ||
		               type == typeof(UIntPtr) ||
		               type == typeof(IntPtr) || type == typeof(DateTime);

		// Defensive check: ensure class types are not accidentally marked as simple
		if (isSimple && type.IsClass && !type.IsEnum && type != typeof(string))
		{
			return false;
		}

		return isSimple;
	}

	private void SerializeExpandoObject(ExpandoObject expando, BinaryWriter writer,
		HashSet<object> serializedObjects,
		Dictionary<object, int> objectMap)
	{
		IDictionary<string, object> dict = expando;
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
			writer.Write(IlTypeSerializer.COMPACT_LAYOUT_TAG);

			var context = new IlTypeSerializer.ObjectSerializationContext
			{
				SerializedObjects = serializedObjects,
				ObjectMap = objectMap,
				Serializer = this
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

			var context = new IlTypeSerializer.ObjectSerializationContext
			{
				SerializedObjects = serializedObjects,
				ObjectMap = objectMap,
				Serializer = this
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
		var context = new IlTypeSerializer.ObjectDeserializationContext
		{
			DeserializedObjects = deserializedObjects,
			Serializer = this,
			ObjectToIdMap = _objectToIdMap
		};

		// Register a placeholder immediately to handle self-references during IL deserialization
		var placeholder = new ForwardReferencePlaceholder(objectId);
		deserializedObjects[objectId] = placeholder;

		// Detect compact layout tag after TypeInfo
		var isCompact = false;
		try
		{
			var b = reader.ReadByte();
			if (b == IlTypeSerializer.COMPACT_LAYOUT_TAG)
			{
				isCompact = true;
			}
			else
			{
				// Not compact: seek one byte back if possible
				if (reader.BaseStream.CanSeek)
				{
					reader.BaseStream.Seek(-1, SeekOrigin.Current);
				}
				// Non-seekable stream without compact tag is not supported for legacy IL path
				// Fall back to legacy path by throwing so caller can handle, but we stay here and continue
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
		
		return result.EndsWith(suffix2, StringComparison.Ordinal) ? result.Substring(0, result.Length - suffix2.Length) : result;
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
		if (constructor != null) return constructor.Invoke(null);

		// For value types, we can use default(T) and box it
		if (type.IsValueType) return Activator.CreateInstance(type)!;

		// Try using System.Runtime.Serialization.ObjectManager for .NET Core/5+
		try
		{
			// For .NET Core 3.0+ and .NET 5+, we can use reflection to access internal methods
			var runtimeType = typeof(Type).Assembly.GetType("System.RuntimeType");
			if (runtimeType != null)
			{
				var getUninitializedObjectMethod = runtimeType.GetMethod("GetUninitializedObject",
					BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
				if (getUninitializedObjectMethod != null)
					return getUninitializedObjectMethod.Invoke(null, [type])!;
			}
		}
		catch
		{
			// Internal method not available or failed
		}

		// Last resort: try to create using the most accessible constructor with default parameters
		var constructors =
			type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
		
		if (constructors.Length <= 0)
			throw new InvalidOperationException(
				$"Cannot create instance of type '{type.FullName}' without a parameterless constructor. Consider adding a parameterless constructor or marking the type with [Serializable].");
		
		// Try the parameterless constructor first (already checked above, but just in case)
		var parameterlessConstructor = constructors.FirstOrDefault(c => c.GetParameters().Length == 0);
		if (parameterlessConstructor != null) return parameterlessConstructor.Invoke(null);

		// Try constructors with parameters and use default values
		foreach (var ctor in constructors.OrderBy(c => c.GetParameters().Length))
		{
			var parameters = ctor.GetParameters();
			var args = new object[parameters.Length];

			var canCreate = true;
			for (var i = 0; i < parameters.Length; i++)
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

			if (!canCreate)
				continue;
				
			try
			{
				return ctor.Invoke(args);
			}
			catch
			{
				// Try next constructor
			}
		}

		throw new InvalidOperationException(
			$"Cannot create instance of type '{type.FullName}' without a parameterless constructor. Consider adding a parameterless constructor or marking the type with [Serializable].");
	}

	private class ReferenceEqualityComparer : IEqualityComparer<object>
	{
		public static readonly ReferenceEqualityComparer Instance = new();

		public new bool Equals(object x, object y)
		{
			return ReferenceEquals(x, y);
		}

		public int GetHashCode(object obj)
		{
			return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
		}
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
	internal void AddPendingForwardReference(object targetObject, FieldInfo field,
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
		// Thread-safe: Use lock to prevent race conditions when multiple threads resolve references
		lock (_pendingForwardReferencesLock)
		{
			// Try multiple passes in case of chains; cap iterations to avoid infinite loops.
			var maxIterations = _pendingForwardReferences.Count + 1;
			for (var iteration = 0; iteration < maxIterations; iteration++)
			{
				if (_pendingForwardReferences.Count == 0) break;

				var remaining =
					new List<(object targetObject, FieldInfo field, int placeholderObjectId)>();

				foreach (var (targetObject, field, placeholderObjectId) in _pendingForwardReferences)
					if (deserializedObjects.TryGetValue(placeholderObjectId, out var resolved)
					    && resolved is not ForwardReferencePlaceholder)
						field.SetValue(targetObject, resolved);
					else
						remaining.Add((targetObject, field, placeholderObjectId));

				_pendingForwardReferences.Clear();
				_pendingForwardReferences.AddRange(remaining);

				if (_pendingForwardReferences.Count == 0) break;
			}
		}
	}

	/// <summary>
	/// Validates stream state and detects potential corruption.
	/// </summary>
	/// <param name="reader">BinaryReader to validate</param>
	/// <param name="context">Context description for error messages</param>
	private void ValidateStreamState(BinaryReader reader, string context)
	{
		if (!reader.BaseStream.CanSeek) return;

		var position = reader.BaseStream.Position;
		var length = reader.BaseStream.Length;

		// Check for stream position corruption
		if (position < 0 || position > length)
			throw new InvalidOperationException(
				$"Stream corruption detected in {context}: Invalid position {position} (stream length: {length})");

		// Check if we're at the end when we expect to read more data
		if (position >= length && context.Contains("Read"))
			throw new InvalidOperationException(
				$"Stream corruption detected in {context}: Attempting to read at end of stream (position: {position}, length: {length})");
	}

	/// <summary>
	/// Validates if a byte is a valid object marker in NeoBinary protocol.
	/// </summary>
	/// <param name="marker">Marker byte to validate</param>
	/// <returns>True if valid marker, false otherwise</returns>
	private static bool IsValidMarker(byte marker)
	{
		return marker switch
		{
			0 => true, // Null marker
			1 => true, // Object marker  
			2 => true, // Reference marker
			3 => true, // Simple object marker
			0xFE => true, // Compact layout tag
			_ => false
		};
	}

	/// <summary>
	/// Attempts to read a string starting from an invalid marker for debugging.
	/// </summary>
	/// <param name="reader">Binary reader positioned after invalid marker</param>
	/// <param name="firstByte">First byte that was invalid marker</param>
	/// <returns>Potential string interpretation of the data</returns>
	private static string TryReadStringFromMarker(BinaryReader reader, byte firstByte)
	{
		try
		{
			// Try to read as if this was start of a string
			var bytes = new List<byte> { firstByte };
			var originalPos = reader.BaseStream.Position;

			// Read up to 50 more bytes to get context
			for (var i = 0; i < 50; i++)
			{
				if (reader.BaseStream.Position >= reader.BaseStream.Length) break;
				var b = reader.ReadByte();
				if (b == 0) break; // Null terminator
				if (b < 32 || b > 126) break; // Non-printable character
				bytes.Add(b);
			}

			reader.BaseStream.Position = originalPos;
			return Encoding.ASCII.GetString(bytes.ToArray());
		}
		catch
		{
			return $"<cannot read: {firstByte:X2}>";
		}
	}

	/// <summary>
	/// Formats byte array as hex dump for debugging corruption issues.
	/// </summary>
	/// <param name="bytes">Byte array to format</param>
	/// <param name="startOffset">Starting offset in the stream</param>
	/// <returns>Formatted hex dump string</returns>
	private static string FormatHexDump(byte[] bytes, long startOffset)
	{
		var sb = new StringBuilder();
		var bytesPerLine = 16;

		for (var i = 0; i < bytes.Length; i += bytesPerLine)
		{
			var lineBytes = Math.Min(bytesPerLine, bytes.Length - i);
			var offset = startOffset + i;

			// Offset column
			sb.Append($"{offset:X8}: ");

			// Hex bytes
			for (var j = 0; j < lineBytes; j++)
			{
				var byteValue = bytes[i + j];
				var isMarkerPosition = offset + j == startOffset - 1;

				if (isMarkerPosition)
					sb.Append(">>"); // Mark the invalid marker

				sb.Append($"{byteValue:X2}");

				if (isMarkerPosition)
					sb.Append("<<"); // Mark the invalid marker

				sb.Append(" ");
			}

			// Padding for incomplete lines
			for (var j = lineBytes; j < bytesPerLine; j++) sb.Append("   ");

			// ASCII representation
			sb.Append(" |");
			for (var j = 0; j < lineBytes; j++)
			{
				var b = bytes[i + j];
				sb.Append(b >= 32 && b <= 126 ? (char)b : '.');
			}

			sb.Append("|\n");
		}

		return sb.ToString();
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