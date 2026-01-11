using System.Collections.Generic;
using System.Reflection;

namespace CoreRemoting.Serialization.NeoBinary;

public partial class IlTypeSerializer
{
	/// <summary>
	/// Deserialization context containing shared state.
	/// </summary>
	public class DeserializationContext
	{
		/// <summary>
		/// A dictionary mapping object IDs to deserialized objects.
		/// Used during deserialization to maintain a cache of already created objects,
		/// preventing the creation of duplicate instances and handling forward references.
		/// </summary>
		public Dictionary<int, object> DeserializedObjects { get; set; }

		/// <summary>
		/// Represents the core serializer used for serialization and deserialization processes.
		/// This serializer integrates with an IlTypeSerializer.DeserializationContext to manage
		/// serialized objects, forward references, and deserialized objects during complex data structures handling.
		/// </summary>
		public NeoBinarySerializer Serializer { get; set; }

		/// <summary>
		/// A list of forward references encountered during deserialization.
		/// Forward references occur when an object is serialized before all of its fields are resolved. This property stores these references to allow proper resolution later.
		/// </summary>
		public List<(object targetObject, FieldInfo field, int placeholderObjectId)> ForwardReferences { get; set; } =
			new();

		/// <summary>
		/// A dictionary used for object-to-ID mapping during deserialization.
		/// Maps objects to their corresponding IDs in the deserialized context. This
		/// is crucial for handling self-references and resolving forward references
		/// efficiently.
		/// </summary>
		public Dictionary<object, int> ObjectToIdMap { get; set; } = new();
	}
}