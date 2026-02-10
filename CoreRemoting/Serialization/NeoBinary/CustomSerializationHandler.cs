using System;
using System.Collections.Generic;

namespace CoreRemoting.Serialization.NeoBinary;

/// <summary>
/// Handler interface for custom serialization of types that cannot implement ICustomSerialization directly.
/// Use this to provide custom serialization logic for third-party types or to avoid hard dependencies on CoreRemoting.
/// </summary>
public class CustomSerializationHandler
{
	/// <summary>
	/// Gets the serialization data for an object.
	/// </summary>
	public Func<object, List<CustomSerializationData>> GetSerializationData { get; set; }

	/// <summary>
	/// Creates an object from serialization data.
	/// </summary>
	public Func<Type, List<CustomSerializationData>, object> CreateFromSerializationData { get; set; }

	/// <summary>
	/// Creates a handler from serializable and deserializable functions.
	/// </summary>
	public static CustomSerializationHandler From(Func<object, List<CustomSerializationData>> getSerializationData,
		Func<Type, List<CustomSerializationData>, object> createFromSerializationData) =>
		new()
		{
			GetSerializationData = getSerializationData,
			CreateFromSerializationData = createFromSerializationData
		};
}