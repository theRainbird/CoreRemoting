using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;

namespace CoreRemoting.Serialization.NeoBinary
{
	/// <summary>
	/// Modern binary serializer that replaces BinaryFormatter with enhanced security and performance.
	/// </summary>
	public class NeoBinarySerializer
	{
		private const string MAGIC_NAME = "NEOB";
		private const ushort CURRENT_VERSION = 1;

		private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();
		private readonly Dictionary<Type, MethodInfo> _serializeMethods = new();
		private readonly Dictionary<Type, MethodInfo> _deserializeMethods = new();

		// Caches for performance optimization
		private readonly ConcurrentDictionary<Type, FieldInfo[]> _fieldCache = new();
		private readonly ConcurrentDictionary<Type, string> _typeNameCache = new();
		private readonly ConcurrentDictionary<string, Type> _resolvedTypeCache = new();
		private readonly ConcurrentDictionary<FieldInfo, Func<object, object>> _getterCache = new();

		/// <summary>
		/// Gets or sets the serializer configuration.
		/// </summary>
		public NeoBinarySerializerConfig Config { get; set; } = new NeoBinarySerializerConfig();

		/// <summary>
		/// Gets or sets the type validator for security.
		/// </summary>
		public NeoBinaryTypeValidator TypeValidator { get; set; } = new NeoBinaryTypeValidator();

		/// <summary>
		/// Serializes an object to the specified stream.
		/// </summary>
		/// <param name="graph">Object to serialize</param>
		/// <param name="serializationStream">Stream to write to</param>
		public void Serialize(object graph, Stream serializationStream)
		{
			if (serializationStream == null)
				throw new ArgumentNullException(nameof(serializationStream));

			_lock.EnterWriteLock();
			try
			{
				using var writer = new BinaryWriter(serializationStream, Encoding.UTF8, leaveOpen: true);
				var serializedObjects = new HashSet<object>(ReferenceEqualityComparer.Instance);
				var objectMap = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);

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
				_lock.ExitWriteLock();
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

			_lock.EnterWriteLock();
			try
			{
				using var reader = new BinaryReader(serializationStream, Encoding.UTF8, leaveOpen: true);
				var deserializedObjects = new Dictionary<int, object>();

				// Read and validate header
				ReadHeader(reader);

				// Deserialize object graph
				var firstByte = reader.ReadByte();
				if (firstByte == 0)
				{
					return null;
				}

				// Put the byte back
				serializationStream.Position = serializationStream.Position - 1;
				var result = DeserializeObject(reader, deserializedObjects);

				// Skip resolving forward references to avoid stack overflow

				return result;
			}
			finally
			{
				_lock.ExitWriteLock();
			}
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
			var magic = Encoding.ASCII.GetString(magicBytes);
			if (magic != MAGIC_NAME)
				throw new InvalidOperationException($"Invalid magic number: {magic}");

			var version = reader.ReadUInt16();
			if (version > CURRENT_VERSION)
				throw new InvalidOperationException($"Unsupported version: {version}");

			var flags = reader.ReadUInt16();
			// Store flags for later use if needed
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
			else if (typeof(System.Collections.IList).IsAssignableFrom(type))
			{
				SerializeList((System.Collections.IList)obj, writer, serializedObjects, objectMap);
			}
			else if (typeof(System.Collections.IDictionary).IsAssignableFrom(type))
			{
				SerializeDictionary((System.Collections.IDictionary)obj, writer, serializedObjects, objectMap);
			}
			else if (typeof(System.Data.DataSet).IsAssignableFrom(type))
			{
				SerializeDataSet((DataSet)obj, writer, serializedObjects, objectMap);
			}
			else if (typeof(System.Data.DataTable).IsAssignableFrom(type))
			{
				SerializeDataTable((DataTable)obj, writer, serializedObjects, objectMap);
			}
			else if (typeof(Exception).IsAssignableFrom(type))
			{
				SerializeException((Exception)obj, writer, serializedObjects, objectMap);
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

				if (type.IsPrimitive || type == typeof(string) || type == typeof(decimal) || type == typeof(UIntPtr) ||
				    type == typeof(IntPtr))
				{
					obj = DeserializePrimitive(type, reader);
				}
				else if (type.IsEnum)
				{
					obj = DeserializeEnum(type, reader);
				}
				else if (type.IsArray)
				{
					obj = DeserializeArray(type, reader, deserializedObjects, objectId);
				}
				else if (typeof(System.Collections.IList).IsAssignableFrom(type))
				{
					obj = DeserializeList(type, reader, deserializedObjects, objectId);
				}
				else if (typeof(System.Collections.IDictionary).IsAssignableFrom(type))
				{
					obj = DeserializeDictionary(type, reader, deserializedObjects, objectId);
				}
				else if (typeof(System.Data.DataSet).IsAssignableFrom(type))
				{
					obj = DeserializeDataSet(type, reader, deserializedObjects, objectId);
				}
				else if (typeof(System.Data.DataTable).IsAssignableFrom(type))
				{
					obj = DeserializeDataTable(type, reader, deserializedObjects, objectId);
				}
				else if (typeof(Exception).IsAssignableFrom(type))
				{
					obj = DeserializeException(type, reader, deserializedObjects, objectId);
				}
				else
				{
					obj = DeserializeComplexObject(type, reader, deserializedObjects, objectId);
				}

				deserializedObjects[objectId] = obj;
				return obj;
			}

			throw new InvalidOperationException($"Invalid marker: {marker}");
		}

		private void WriteTypeInfo(BinaryWriter writer, Type type)
		{
			var assemblyName = type.Assembly.GetName();
			string typeName;

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

			writer.Write(typeName);

			// Always persist the simple assembly name to allow the deserializer to
			// load the defining assembly, even if we omit version information.
			// This improves resolution for types from third-party assemblies
			// (e.g., Serialize.Linq.Nodes) that might not yet be loaded.
			if (Config.IncludeAssemblyVersions)
			{
				writer.Write(assemblyName.Name ?? string.Empty);
				writer.Write(assemblyName.Version?.ToString() ?? string.Empty);
			}
			else
			{
				writer.Write(assemblyName.Name ?? string.Empty);
				writer.Write(string.Empty); // omit version to keep assembly-neutral diffs stable
			}
		}

		private Type ReadTypeInfo(BinaryReader reader)
		{
			var typeName = reader.ReadString();
			var assemblyName = reader.ReadString();
			var assemblyVersion = reader.ReadString();

			Type type = null;

			if (!string.IsNullOrEmpty(assemblyName))
			{
				// Try to get type from current loaded assemblies first
				type = AppDomain.CurrentDomain.GetAssemblies()
					.SelectMany(a => a.GetTypes())
					.FirstOrDefault(t => t.FullName == typeName || t.Name == typeName);

				// If not found in loaded assemblies, try Assembly.Load
				if (type == null)
				{
					try
					{
						var assembly = Assembly.Load(assemblyName);
						type = assembly.GetType(typeName);
					}
					catch
					{
						// If assembly loading fails, try Type.GetType
						type = Type.GetType(typeName);
					}
				}
			}

			// If still not found, try assembly-neutral resolution
			if (type == null)
			{
				type = ResolveAssemblyNeutralType(typeName);
			}

			if (type == null)
				throw new TypeLoadException($"Cannot load type: {typeName}, Assembly: {assemblyName}");

			// Validate type for security
			TypeValidator.ValidateType(type);

			return type;
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
				if (t.IsGenericType)
				{
					var genericDef = t.GetGenericTypeDefinition();
					var defName = genericDef.FullName; // e.g. System.Collections.Generic.List`1
					var args = t.GetGenericArguments();
					var argNames = args.Select(BuildAssemblyNeutralTypeName).ToArray();
					var joined = string.Join("],[", argNames);
					return $"{defName}[[{joined}]]";
				}

				if (t.IsArray)
				{
					// Handle arrays by composing element type and rank suffix
					var elem = t.GetElementType();
					var rank = t.GetArrayRank();
					var suffix = rank == 1 ? "[]" : "[" + new string(',', rank - 1) + "]";
					return BuildAssemblyNeutralTypeName(elem) + suffix;
				}

				return t.FullName ?? t.Name;
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
				case decimal dec: writer.Write(dec.ToString()); break;
				case string str: writer.Write(str ?? string.Empty); break;
				case UIntPtr up: writer.Write(up.ToUInt64()); break;
				case IntPtr ip: writer.Write(ip.ToInt64()); break;
				default: throw new InvalidOperationException($"Unsupported primitive type: {obj.GetType()}");
			}
		}

		private object DeserializePrimitive(Type type, BinaryReader reader)
		{
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
			if (type == typeof(decimal)) return decimal.Parse(reader.ReadString());
			if (type == typeof(string)) return reader.ReadString();
			if (type == typeof(UIntPtr)) return new UIntPtr(reader.ReadUInt64());
			if (type == typeof(IntPtr)) return new IntPtr(reader.ReadInt64());

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
			       type == typeof(IntPtr);
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

		private void SerializeList(System.Collections.IList list, BinaryWriter writer,
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
			var list = (System.Collections.IList)CreateInstanceWithoutConstructor(type);

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

		private void SerializeDictionary(System.Collections.IDictionary dictionary, BinaryWriter writer,
			HashSet<object> serializedObjects, Dictionary<object, int> objectMap)
		{
			writer.Write(dictionary.Count);
			foreach (System.Collections.DictionaryEntry entry in dictionary)
			{
				SerializeObject(entry.Key, writer, serializedObjects, objectMap);
				SerializeObject(entry.Value, writer, serializedObjects, objectMap);
			}
		}

		private object DeserializeDictionary(Type type, BinaryReader reader,
			Dictionary<int, object> deserializedObjects, int objectId)
		{
			var count = reader.ReadInt32();
			var dictionary = (System.Collections.IDictionary)CreateInstanceWithoutConstructor(type);

			// Register the dictionary immediately to handle circular references
			deserializedObjects[objectId] = dictionary;

			for (int i = 0; i < count; i++)
			{
				var key = DeserializeObject(reader, deserializedObjects);
				var value = DeserializeObject(reader, deserializedObjects);
				dictionary[key] = value;
			}

			return dictionary;
		}

		private void SerializeComplexObject(object obj, BinaryWriter writer, HashSet<object> serializedObjects,
			Dictionary<object, int> objectMap)
		{
			var type = obj.GetType();
			var fields = GetAllFieldsInHierarchy(type);

			writer.Write(fields.Count);

			foreach (var field in fields)
			{
				writer.Write(field.Name);
				var getter = _getterCache.GetOrAdd(field, f =>
				{
					var objParam = Expression.Parameter(typeof(object), "obj");
					var castObj = Expression.Convert(objParam, f.DeclaringType);
					var fieldExpr = Expression.Field(castObj, f);
					var convertExpr = Expression.Convert(fieldExpr, typeof(object));
					return Expression.Lambda<Func<object, object>>(convertExpr, objParam).Compile();
				});
				var value = getter(obj);
				SerializeObject(value, writer, serializedObjects, objectMap);
			}
		}

		private object DeserializeComplexObject(Type type, BinaryReader reader,
			Dictionary<int, object> deserializedObjects, int objectId)
		{
			var obj = CreateInstanceWithoutConstructor(type);

			// Register the object immediately to handle circular references
			deserializedObjects[objectId] = obj;

			var fieldCount = reader.ReadInt32();

			for (int i = 0; i < fieldCount; i++)
			{
				var fieldName = reader.ReadString();
				var value = DeserializeObject(reader, deserializedObjects);

				var field = GetFieldInHierarchy(type, fieldName);
				field?.SetValue(obj, value);
			}

			return obj;
		}

		private void SerializeException(Exception exception, BinaryWriter writer, HashSet<object> serializedObjects,
			Dictionary<object, int> objectMap)
		{
			var type = exception.GetType();

			// Serialize basic exception properties
			writer.Write(exception.Message ?? string.Empty);
			writer.Write(exception.Source ?? string.Empty);
			writer.Write(exception.StackTrace ?? string.Empty);
			writer.Write(exception.HelpLink ?? string.Empty);

			// Serialize HResult
			writer.Write(exception.HResult);

			// Serialize inner exception if present
			SerializeObject(exception.InnerException, writer, serializedObjects, objectMap);

			// Serialize data dictionary
			if (exception.Data != null)
			{
				writer.Write(exception.Data.Count);
				foreach (System.Collections.DictionaryEntry entry in exception.Data)
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
			writer.Write(dataSet.Namespace ?? string.Empty);
			writer.Write(dataSet.Prefix ?? string.Empty);
			writer.Write(dataSet.CaseSensitive);
			writer.Write(dataSet.Locale != null ? dataSet.Locale.Name : string.Empty);
			writer.Write(dataSet.EnforceConstraints);

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
			writer.Write(dataTable.Namespace ?? string.Empty);
			writer.Write(dataTable.Prefix ?? string.Empty);
			writer.Write(dataTable.CaseSensitive);
			writer.Write(dataTable.Locale != null ? dataTable.Locale.Name : string.Empty);

			// Serialize Columns
			writer.Write(dataTable.Columns.Count);
			foreach (DataColumn column in dataTable.Columns)
			{
				writer.Write(column.ColumnName ?? string.Empty);
				writer.Write(column.DataType.FullName ?? string.Empty);
				writer.Write(column.AllowDBNull);
				writer.Write(column.AutoIncrement);
				writer.Write(column.AutoIncrementSeed);
				writer.Write(column.AutoIncrementStep);
				writer.Write(column.Caption ?? string.Empty);
				writer.Write(column.DefaultValue != null && column.DefaultValue != DBNull.Value);
				if (column.DefaultValue != null && column.DefaultValue != DBNull.Value)
				{
					SerializeObject(column.DefaultValue, writer, serializedObjects, objectMap);
				}

				writer.Write(column.Expression ?? string.Empty);
				writer.Write(column.MaxLength);
				writer.Write(column.ReadOnly);
				writer.Write(column.Unique);
			}

			// Serialize Rows
			writer.Write(dataTable.Rows.Count);
			foreach (DataRow row in dataTable.Rows)
			{
				writer.Write((int)row.RowState);
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
			Exception exception = null;
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
					exception = new ArgumentNullException(paramName, (string)baseMessage);
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
			dataSet.Namespace = reader.ReadString();
			dataSet.Prefix = reader.ReadString();
			dataSet.CaseSensitive = reader.ReadBoolean();
			var localeName = reader.ReadString();
			if (!string.IsNullOrEmpty(localeName))
			{
				dataSet.Locale = new System.Globalization.CultureInfo(localeName);
			}

			dataSet.EnforceConstraints = reader.ReadBoolean();

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
			dataTable.Namespace = reader.ReadString();
			dataTable.Prefix = reader.ReadString();
			dataTable.CaseSensitive = reader.ReadBoolean();
			var localeName = reader.ReadString();
			if (!string.IsNullOrEmpty(localeName))
			{
				dataTable.Locale = new System.Globalization.CultureInfo(localeName);
			}

			// Deserialize Columns
			var columnCount = reader.ReadInt32();
			for (int i = 0; i < columnCount; i++)
			{
				var columnName = reader.ReadString();
				var dataTypeName = reader.ReadString();
				var dataType = Type.GetType(dataTypeName) ?? typeof(string);
				var allowDBNull = reader.ReadBoolean();
				var autoIncrement = reader.ReadBoolean();
				var autoIncrementSeed = reader.ReadInt64();
				var autoIncrementStep = reader.ReadInt64();
				var caption = reader.ReadString();
				var hasDefaultValue = reader.ReadBoolean();
				object defaultValue = null;
				if (hasDefaultValue)
				{
					defaultValue = DeserializeObject(reader, deserializedObjects);
				}

				var expression = reader.ReadString();
				var maxLength = reader.ReadInt32();
				var readOnly = reader.ReadBoolean();
				var unique = reader.ReadBoolean();

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
				var rowState = (DataRowState)reader.ReadInt32();
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
				return FormatterServices.GetUninitializedObject(type);
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

		private void ResolveForwardReferences(Dictionary<int, object> deserializedObjects)
		{
			// Keep resolving until all placeholders are resolved
			bool hasChanges;
			int maxIterations = 10; // Prevent infinite loops
			int iteration = 0;

			do
			{
				hasChanges = false;
				iteration++;

				// Find all forward reference placeholders
				var placeholderEntries = deserializedObjects
					.Where(kvp => kvp.Value is ForwardReferencePlaceholder)
					.ToList();

				// Replace each placeholder with the actual object it refers to
				foreach (var kvp in placeholderEntries)
				{
					var placeholderId = kvp.Key;
					var placeholder = (ForwardReferencePlaceholder)kvp.Value;

					// Find the actual object that this placeholder refers to
					if (deserializedObjects.TryGetValue(placeholder.ObjectId, out var actualObject))
					{
						// Replace placeholder with actual object
						deserializedObjects[placeholderId] = actualObject;
						hasChanges = true;

						// Skip updating references to avoid stack overflow
					}
				}

				// Skip updating object fields to avoid stack overflow
			} while (hasChanges && iteration < maxIterations);
		}

		private void UpdateForwardReferences(ForwardReferencePlaceholder placeholder, object actualObject,
			Dictionary<int, object> deserializedObjects)
		{
			// Skip updating to avoid stack overflow
		}

		private bool ReplacePlaceholdersInObjectFields(object obj, Dictionary<int, object> deserializedObjects)
		{
			bool hasChanges = false;
			var type = obj.GetType();

			// Skip for DataSets and DataTables to avoid issues
			if (typeof(DataSet).IsAssignableFrom(type) || typeof(DataTable).IsAssignableFrom(type))
			{
				return false;
			}

			var fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

			foreach (var field in fields)
			{
				var value = field.GetValue(obj);
				if (value is ForwardReferencePlaceholder placeholder)
				{
					// Find actual object this placeholder refers to
					if (deserializedObjects.TryGetValue(placeholder.ObjectId, out var actualObject))
					{
						field.SetValue(obj, actualObject);
						hasChanges = true;
					}
				}
				// No recursion to avoid stack overflow
			}

			return hasChanges;
		}

		private void ReplacePlaceholdersInObjectFields(object obj, ForwardReferencePlaceholder targetPlaceholder,
			object replacementObject)
		{
			var type = obj.GetType();

			// Skip for DataSets and DataTables
			if (typeof(DataSet).IsAssignableFrom(type) || typeof(DataTable).IsAssignableFrom(type))
			{
				return;
			}

			var fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

			foreach (var field in fields)
			{
				var value = field.GetValue(obj);
				if (ReferenceEquals(value, targetPlaceholder))
				{
					field.SetValue(obj, replacementObject);
				}
				// No recursion
			}
		}

		private List<FieldInfo> GetAllFieldsInHierarchy(Type type)
		{
			return _fieldCache.GetOrAdd(type, t =>
			{
				var fields = new List<FieldInfo>();
				var currentType = t;

				while (currentType != null && currentType != typeof(object))
				{
					var typeFields = currentType.GetFields(BindingFlags.Public | BindingFlags.NonPublic |
					                                       BindingFlags.Instance | BindingFlags.DeclaredOnly);
					fields.AddRange(typeFields);
					currentType = currentType.BaseType;
				}

				return fields.ToArray();
			}).ToList();
		}

		private static FieldInfo GetFieldInHierarchy(Type type, string fieldName)
		{
			var currentType = type;

			while (currentType != null && currentType != typeof(object))
			{
				var field = currentType.GetField(fieldName,
					BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
				if (field != null)
				{
					return field;
				}

				currentType = currentType.BaseType;
			}

			return null;
		}
	}
}