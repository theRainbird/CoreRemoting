using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;

namespace CoreRemoting.Serialization.NeoBinary;

partial class NeoBinarySerializer
{
	private const byte CUSTOM_SERIALIZABLE_MARKER = 4;

	private void SerializeCustomSerializableObject(object obj, BinaryWriter writer, HashSet<object> serializedObjects,
		Dictionary<object, int> objectMap)
	{
		var customObj = (ICustomSerialization)obj;

		List<CustomSerializationData> data = customObj.GetSerializationData();

		writer.Write(data.Count);

		foreach (var entry in data)
			SerializeCustomSerializationData(entry, writer, serializedObjects, objectMap);
	}

	private void SerializeCustomSerializationData(CustomSerializationData data, BinaryWriter writer,
		HashSet<object> serializedObjects, Dictionary<object, int> objectMap)
	{
		writer.Write(data.Name ?? string.Empty);
		WriteTypeInfo(writer, data.Type);
		SerializeObject(data.Value, writer, serializedObjects, objectMap);
	}

	private object DeserializeCustomSerializableObject(Type type, BinaryReader reader,
		Dictionary<int, object> deserializedObjects, int objectId)
	{
		int dataCount = reader.ReadInt32();

		var data = new List<CustomSerializationData>(dataCount);
		for (int i = 0; i < dataCount; i++)
			data.Add(DeserializeCustomSerializationData(reader, deserializedObjects));

		var ctor = type.GetConstructor(
			BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance,
			null,
			[typeof(List<CustomSerializationData>)],
			null);

		if (ctor == null)
			throw new SerializationException(
				$"Type '{type.FullName}' implements ICustomSerialization but has no " +
				$"constructor with 'List<CustomSerializationData>' parameter.");

		object obj = ctor.Invoke([data]);

		deserializedObjects[objectId] = obj;

		return obj;
	}

	private CustomSerializationData DeserializeCustomSerializationData(BinaryReader reader,
		Dictionary<int, object> deserializedObjects)
	{
		string name = reader.ReadString();
		Type type = ReadTypeInfo(reader);
		object value = DeserializeObject(reader, deserializedObjects);

		return new CustomSerializationData
		{
			Name = name,
			Type = type,
			Value = value
		};
	}
}