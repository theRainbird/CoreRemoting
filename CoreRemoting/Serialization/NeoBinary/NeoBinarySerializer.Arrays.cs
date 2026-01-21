using System;
using System.Collections;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;

namespace CoreRemoting.Serialization.NeoBinary;

partial class NeoBinarySerializer
{
	private int[] GetIndicesFromLinearIndex(int linearIndex, int[] lengths)
	{
		var indices = new int[lengths.Length];
		var remaining = linearIndex;

		for (var i = lengths.Length - 1; i >= 0; i--)
		{
			indices[i] = remaining % lengths[i];
			remaining /= lengths[i];
		}

		return indices;
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
				for (var i = 0; i < Vector<int>.Count; i++)
					writer.Write(vector[i]);
			// Handle remainder
			var remainder = array.Length % Vector<int>.Count;
			for (var i = array.Length - remainder; i < array.Length; i++)
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
				for (var i = 0; i < Vector<float>.Count; i++)
					writer.Write(vector[i]);
			var remainder = array.Length % Vector<float>.Count;
			for (var i = array.Length - remainder; i < array.Length; i++)
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
				for (var i = 0; i < Vector<double>.Count; i++)
					writer.Write(vector[i]);
			var remainder = array.Length % Vector<double>.Count;
			for (var i = array.Length - remainder; i < array.Length; i++)
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
			var vectorIndex = 0;
			foreach (var vector in vectors)
				for (var i = 0; i < Vector<int>.Count; i++)
					array[vectorIndex++] = reader.ReadInt32();
			var remainder = array.Length % Vector<int>.Count;
			for (var i = array.Length - remainder; i < array.Length; i++)
				array[i] = reader.ReadInt32();
		}
		else
#endif
		{
			for (var i = 0; i < array.Length; i++) array[i] = reader.ReadInt32();
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
			var vectorIndex = 0;
			foreach (var vector in vectors)
				for (var i = 0; i < Vector<float>.Count; i++)
					array[vectorIndex++] = reader.ReadSingle();
			var remainder = array.Length % Vector<float>.Count;
			for (var i = array.Length - remainder; i < array.Length; i++)
				array[i] = reader.ReadSingle();
		}
		else
#endif
		{
			for (var i = 0; i < array.Length; i++) array[i] = reader.ReadSingle();
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
			var vectorIndex = 0;
			foreach (var vector in vectors)
				for (var i = 0; i < Vector<double>.Count; i++)
					array[vectorIndex++] = reader.ReadDouble();
			var remainder = array.Length % Vector<double>.Count;
			for (var i = array.Length - remainder; i < array.Length; i++)
				array[i] = reader.ReadDouble();
		}
		else
#endif
		{
			for (var i = 0; i < array.Length; i++) array[i] = reader.ReadDouble();
		}
	}

	private void SerializeArray(Array array, BinaryWriter writer, HashSet<object> serializedObjects,
		Dictionary<object, int> objectMap)
	{
		writer.Write(array.Rank);
		for (var i = 0; i < array.Rank; i++) writer.Write(array.GetLength(i));

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

				if (elementType == typeof(float))
				{
					SerializeFloatArraySimd((float[])array, writer);
					return;
				}

				if (elementType == typeof(double))
				{
					SerializeDoubleArraySimd((double[])array, writer);
					return;
				}
			}

			// Fallback: Element-wise serialization
			for (var i = 0; i < length; i++)
			{
				var element = array.GetValue(i);
				if (isSimpleElement)
					SerializePrimitive(element, writer);
				else
					SerializeObject(element, writer, serializedObjects, objectMap);
			}
		}
		else
		{
			var indices = new int[array.Rank];
			for (var i = 0; i < length; i++)
			{
				var element = array.GetValue(indices);
				if (isSimpleElement)
					SerializePrimitive(element, writer);
				else
					SerializeObject(element, writer, serializedObjects, objectMap);

				IncrementArrayIndices(indices, array);
			}
		}
	}

	private void IncrementArrayIndices(int[] indices, Array array)
	{
		for (var dim = array.Rank - 1; dim >= 0; dim--)
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
		// Backward-compatibility: Some legacy payloads wrote byte[] as [length][raw-bytes]
		// without our current array header (rank + lengths + totalLength).
		// To support that, we first read one Int32 and decide whether it's a rank or a length.
		int rank;
		var firstInt = reader.ReadInt32();
		var elementType = type.GetElementType()!;

		// Heuristic: For byte[] the rank must be 1. If the firstInt is not 1 and is non-negative,
		// interpret it as legacy "length" and read raw bytes directly.
		if (elementType == typeof(byte) && firstInt != 1 && firstInt >= 0)
		{
			var length = firstInt;

			// Bounds check: ensure enough data available
			if (reader.BaseStream.CanSeek)
			{
				var remaining = reader.BaseStream.Length - reader.BaseStream.Position;
				if (length > remaining)
					throw new InvalidOperationException(
						$"Corrupted stream: Declared byte[] length {length} exceeds remaining bytes {remaining}.");
			}

			var bytes = new byte[length];
			var actuallyRead = reader.Read(bytes, 0, length);
			if (actuallyRead != length)
				throw new EndOfStreamException(
					$"Unexpected end of stream while reading legacy byte[]: expected {length}, got {actuallyRead}");

			// Register array immediately to handle circular references
			if (objectId >= 0)
				deserializedObjects[objectId] = bytes;

			return bytes;
		}

		// Ambiguity resolver: For byte[] and firstInt == 1 (could be rank or legacy length=1)
		if (elementType == typeof(byte) && firstInt == 1 && reader.BaseStream.CanSeek)
		{
			var posAfterFirst = reader.BaseStream.Position;
			var remaining = reader.BaseStream.Length - posAfterFirst;
			if (remaining < 8)
			{
				// Not enough bytes for standard header (length + totalLength) -> treat as legacy length=1
				var bytes = new byte[1];
				var actuallyRead = reader.Read(bytes, 0, 1);
				if (actuallyRead != 1)
					throw new EndOfStreamException(
						"Unexpected end of stream while reading legacy byte[] with length 1.");
				if (objectId >= 0)
					deserializedObjects[objectId] = bytes;
				return bytes;
			}

			// Peek header ints to validate plausibility
			var savePos = reader.BaseStream.Position;
			var length1 = reader.ReadInt32();
			var total1 = reader.ReadInt32();
			var remainingAfterHeader = reader.BaseStream.Length - reader.BaseStream.Position;

			// Validate basic plausibility for standard header
			var plausible = length1 >= 0 && total1 == length1 && remainingAfterHeader >= length1;

			// Reset position for standard path
			reader.BaseStream.Position = posAfterFirst;

			if (!plausible)
			{
				// Treat as legacy with length=1
				var bytes = new byte[1];
				var actuallyRead = reader.Read(bytes, 0, 1);
				if (actuallyRead != 1)
					throw new EndOfStreamException(
						"Unexpected end of stream while reading legacy byte[] with length 1.");
				if (objectId >= 0)
					deserializedObjects[objectId] = bytes;
				return bytes;
			}
		}

		rank = firstInt;
		var lengths = new int[rank];
		for (var i = 0; i < rank; i++) lengths[i] = reader.ReadInt32();

		var totalLength = reader.ReadInt32();
		var array = Array.CreateInstance(elementType, lengths);

		// Register array immediately to handle circular references
		if (objectId >= 0)
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

			if (elementType == typeof(float))
			{
				DeserializeFloatArraySimd((float[])array, reader);
				return array;
			}

			if (elementType == typeof(double))
			{
				DeserializeDoubleArraySimd((double[])array, reader);
				return array;
			}
		}

		// Fallback: Element-wise deserialization
		for (var i = 0; i < totalLength; i++)
		{
			var indices = GetIndicesFromLinearIndex(i, lengths);
			object element;

			if (isSimpleElement)
				element = DeserializePrimitive(elementType, reader);
			else
				element = DeserializeObject(reader, deserializedObjects);

			array.SetValue(element, indices);
		}

		return array;
	}

	private void SerializeList(IList list, BinaryWriter writer,
		HashSet<object> serializedObjects, Dictionary<object, int> objectMap)
	{
		writer.Write(list.Count);

		for (var i = 0; i < list.Count; i++) SerializeObject(list[i], writer, serializedObjects, objectMap);
	}

	private object DeserializeList(Type type, BinaryReader reader, Dictionary<int, object> deserializedObjects,
		int objectId)
	{
		var count = reader.ReadInt32();
		var list = (IList)CreateInstanceWithoutConstructor(type);

		// Register the list immediately to handle circular references
		deserializedObjects[objectId] = list;

		for (var i = 0; i < count; i++)
		{
			var item = DeserializeObject(reader, deserializedObjects);

			// If item is a forward reference placeholder, add null for now
			// It will be resolved later
			if (item is ForwardReferencePlaceholder)
				list.Add(null);
			else
				list.Add(item);
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

			for (var i = 0; i < count; i++)
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

		for (var i = 0; i < count; i++)
		{
			var key = DeserializeObject(reader, deserializedObjects);
			var value = DeserializeObject(reader, deserializedObjects);
			dictionary[key] = value;
		}

		return dictionaryObj;
	}
}