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
        if (string.IsNullOrEmpty(fullTypeName))
            return null;

        var resultType = Type.GetType(fullTypeName);
        if (resultType != null)
            return resultType;

        if (!TrySplitTypeAndAssembly(fullTypeName, out var typeName, out var assemblyQualifiedName))
            return null;

        string assemblySimpleName = GetAssemblySimpleNameSafe(assemblyQualifiedName);
        if (string.IsNullOrEmpty(assemblySimpleName))
            return null;

        var assemblies = AppDomain.CurrentDomain.GetAssemblies();

        Assembly targetAssembly = assemblies.FirstOrDefault(a =>
            a.GetName().Name?.Equals(assemblySimpleName, StringComparison.OrdinalIgnoreCase) == true);

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
                var elementType = FindType(assembly, elementTypeName);
                if (elementType != null)
                {
                    return elementType.MakeArrayType();
                }
            }
            
            var type = assembly.GetType(fullTypeName);
            if (type != null) return type;

            if (GetBaseAndArgumentTypesNames(fullTypeName, out var baseTypeName, out var argumentTypeName))
            {
                Type baseType = assembly.GetType(baseTypeName);
                if (baseType != null)
                {
                    Type argType = FindType(assembly, argumentTypeName);
                    if (argType != null)
                    {
                        return baseType.MakeGenericType(argType);
                    }
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    #region Help Methods

    internal bool TrySplitTypeAndAssembly(string fullTypeName, out string typeName, out string assemblyQualifiedName)
    {
        typeName = null;
        assemblyQualifiedName = null;

        int bracketDepth = 0;
        int commaIndex = -1;

        for (int i = 0; i < fullTypeName.Length; i++)
        {
            char c = fullTypeName[i];

            if (c == '[')
            {
                bracketDepth++;
            }
            else if (c == ']')
            {
                bracketDepth--;
            }
            else if (c == ',' && bracketDepth == 0)
            {
                commaIndex = i;
                break;
            }
        }

        if (commaIndex <= 0)
            return false;

        typeName = fullTypeName.Substring(0, commaIndex).Trim();
        assemblyQualifiedName = fullTypeName.Substring(commaIndex + 1).Trim();
        return true;
    }

    internal string GetAssemblySimpleNameSafe(string assemblyQualifiedName)
    {
        if (string.IsNullOrEmpty(assemblyQualifiedName))
            return null;

        try
        {
            var assemblyName = new AssemblyName(assemblyQualifiedName);
            return assemblyName.Name;
        }
        catch
        {
            var firstCommaIndex = assemblyQualifiedName.IndexOf(',');
            if (firstCommaIndex > 0)
            {
                var simpleName = assemblyQualifiedName.Substring(0, firstCommaIndex).Trim();
                return simpleName;
            }

            var versionIndex = assemblyQualifiedName.IndexOf("Version=", StringComparison.OrdinalIgnoreCase);
            if (versionIndex > 0)
            {
                var name = assemblyQualifiedName.Substring(0, versionIndex - 1).Trim();
                name = name.TrimEnd(',');
                return name;
            }
            return null;
        }
    }

    internal bool GetBaseAndArgumentTypesNames(string fullTypeName, out string baseTypeName,
        out string argumentTypeName)
    {
        baseTypeName = null;
        argumentTypeName = null;

        if (string.IsNullOrEmpty(fullTypeName))
            return false;

        int indexOfBracket = fullTypeName.IndexOf('[');
        if (indexOfBracket < 0)
            return false;

        baseTypeName = fullTypeName.Substring(0, indexOfBracket).Trim();

        int lastIndexOfBracket = fullTypeName.LastIndexOf(']');
        if (lastIndexOfBracket <= indexOfBracket)
            return false;

        argumentTypeName = fullTypeName.Substring(indexOfBracket + 1, lastIndexOfBracket - indexOfBracket - 1);

        while (argumentTypeName.StartsWith("[") && argumentTypeName.EndsWith("]"))
        {
            argumentTypeName = argumentTypeName.Substring(1, argumentTypeName.Length - 2);
        }

        return true;
    }

    #endregion
}
