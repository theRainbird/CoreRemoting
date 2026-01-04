
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace CoreRemoting.Serialization.NeoBinary
{
	partial class NeoBinarySerializer
	{
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
	}
}

