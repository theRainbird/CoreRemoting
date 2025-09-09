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
        string simpleTypeName = fullTypeName.Substring(0, commaIndex).Trim();
        string assemblyName = new AssemblyName(fullTypeName.Substring(commaIndex + 1).Trim()).Name;
        resultType = FindTypeInLoadedAssemblies(simpleTypeName, assemblyName);

        return resultType;
    }
        
    private static Type FindTypeInLoadedAssemblies(string typeName, string assemblyName)
    {
        return (from assembly in AppDomain.CurrentDomain.GetAssemblies()
            where assembly.FullName != null && assembly.FullName.Contains(assemblyName)
            select assembly.GetType(typeName)).FirstOrDefault(foundType => foundType != null);
    }
}