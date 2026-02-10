using System.Collections.Generic;

namespace CoreRemoting.Serialization.NeoBinary;

/// <summary>
/// Interface for objects that want to control their own serialization in NeoBinarySerializer.
/// Implementations must provide a constructor with signature: protected T(List[CustomSerializationData] data)
/// 
/// Note: Circular references are not supported for ICustomSerialization objects.
/// </summary>
public interface ICustomSerialization
{
	/// <summary>
	/// Returns the data that should be serialized for this object.
	/// </summary>
	List<CustomSerializationData> GetSerializationData();
}