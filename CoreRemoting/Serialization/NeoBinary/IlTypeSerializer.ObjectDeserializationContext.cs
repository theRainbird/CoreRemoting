using System.Collections.Generic;
using System.Reflection;

namespace CoreRemoting.Serialization.NeoBinary;

public partial class IlTypeSerializer
{
	/// <summary>
	/// Object deserialization context containing shared state for deserialization operations.
	/// Each deserialization operation gets its own isolated context to ensure thread safety.
	/// </summary>
	public class ObjectDeserializationContext
	{
		/// <summary>
		/// Dictionary mapping placeholder object IDs to their deserialized instances.
		/// This ensures object references are correctly resolved during deserialization.
		/// </summary>
		public Dictionary<int, object> DeserializedObjects { get; set; } = new();

	/// <summary>
	/// A dictionary used for object-to-ID mapping during deserialization.
	/// Maps objects to their corresponding IDs in the deserialized context. This
	/// is crucial for handling self-references and resolving forward references
	/// efficiently.
	/// Each deserialization operation gets its own isolated dictionary for thread safety.
	/// </summary>
	public Dictionary<object, int> ObjectToIdMap { get; set; } = new();

		/// <summary>
		/// Forward references that couldn't be resolved during the main deserialization pass.
		/// These are processed by the serializer after the main deserialization is complete.
		/// </summary>
		public List<(object targetObject, FieldInfo field, int placeholderObjectId)> ForwardReferences { get; } = [];

		/// <summary>
		/// Reference to the serializer instance, allowing access to its advanced methods
		/// for handling complex deserialization scenarios.
		/// </summary>
		public NeoBinarySerializer Serializer { get; set; }
	}
}