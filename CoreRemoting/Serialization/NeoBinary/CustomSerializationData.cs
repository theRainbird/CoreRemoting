using System;
using System.Collections.Generic;

namespace CoreRemoting.Serialization.NeoBinary;

/// <summary>
/// Represents a single entry in custom serialization data.
/// Used by ICustomSerialization to specify what data should be serialized.
/// </summary>
public class CustomSerializationData
{
	/// <summary>
	/// Gets or sets the field/property name for the serialization entry.
	/// </summary>
	public string Name { get; set; }

	/// <summary>
	/// Gets or sets the type of the value.
	/// </summary>
	public Type Type { get; set; }

	/// <summary>
	/// Gets or sets the value to serialize.
	/// </summary>
	public object Value { get; set; }
}