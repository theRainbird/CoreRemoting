using System;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;

namespace CoreRemoting.Serialization.Bson;

internal class TypeIgnoreVersionConverter : JsonConverter
{
    public override bool CanConvert(Type objectType)
    {
        return objectType == typeof(Type);
    }

    public override object ReadJson(JsonReader reader, Type objectType, object existingValue,
        JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null)
        {
            return null;
        }

        string typeFullName = reader.Value as string;

        Type resultType = GetTypeWithoutVersion(typeFullName);

        return resultType;
    }

    public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
    {
        writer.WriteValue(((Type)value)?.AssemblyQualifiedName);
    }

    private Type GetTypeWithoutVersion(string fullTypeName)
    {
        var resultType = Type.GetType(fullTypeName);

        if (resultType != null || string.IsNullOrEmpty(fullTypeName))
            return resultType;

        var commaIndex = fullTypeName.IndexOf(',');
        if (commaIndex <= 0)
            return null;

        string typeName = fullTypeName.Substring(0, commaIndex).Trim();
        string assemblyQualifiedName = fullTypeName.Substring(commaIndex + 1).Trim();
        string assemblySimpleName = new AssemblyName(assemblyQualifiedName).Name;

        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        Assembly targetAssembly = null;
        foreach (var assembly in assemblies)
        {
            var name = assembly.GetName().Name;
            if (name != null && name.Equals(assemblySimpleName, StringComparison.OrdinalIgnoreCase))
            {
                targetAssembly = assembly;
                break;
            }
        }

        if (targetAssembly != null)
        {
            resultType = FindType(targetAssembly, typeName);
            if (resultType != null) return resultType;
        }

        foreach (var assembly in assemblies)
        {
            resultType = FindType(assembly, typeName);
            if (resultType != null) return resultType;
        }
        return null;
    }

    private Type FindType(Assembly assembly, string fullTypeName)
    {
        try
        {
            if (fullTypeName.EndsWith("[]"))
            {
                var elementTypeName = fullTypeName.Substring(0, fullTypeName.Length - 2);
                var elementType = assembly.GetType(elementTypeName) ?? FindGenericType(assembly, elementTypeName);
                if (elementType != null)
                {
                    return elementType.MakeArrayType();
                }
            }

            var type = assembly.GetType(fullTypeName);
            if (type != null) return type;

            return FindGenericType(assembly, fullTypeName);
        }
        catch
        {
            return null;
        }
    }

    private Type FindGenericType(Assembly assembly, string fullTypeName)
    {
        try
        {
            if (!fullTypeName.Contains('['))
                return assembly.GetType(fullTypeName);

            int indexOfBracket = fullTypeName.IndexOf('[');
            string baseTypeName = fullTypeName.Substring(0, indexOfBracket).Trim();
            string argumentTypeName = fullTypeName.Substring(indexOfBracket + 1, fullTypeName.Length - indexOfBracket - 2);

            Type baseType = assembly.GetType(baseTypeName);
            if (baseType == null)
                return null;

            Type argType = FindGenericType(assembly, argumentTypeName);
            if (argType == null)
                return null;

            return baseType.MakeGenericType(argType);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
