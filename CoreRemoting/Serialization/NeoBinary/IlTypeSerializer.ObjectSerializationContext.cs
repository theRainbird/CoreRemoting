using System.Collections.Generic;

namespace CoreRemoting.Serialization.NeoBinary;

public partial class IlTypeSerializer
{
	/// <summary>
	/// Object serialization context containing shared state for serialization operations.
	/// </summary>
	public class ObjectSerializationContext
	{
		/// <summary>
		/// A collection of objects that have already been serialized in the current context.
		/// Used to avoid duplicate serialization of the same object.
		/// </summary>
		public HashSet<object> SerializedObjects { get; set; }

		/// <summary>
		/// A dictionary mapping objects to their unique identifiers in the current serialization context.
		/// Used to track and manage object references during serialization and deserialization processes.
		/// </summary>
		public Dictionary<object, int> ObjectMap { get; set; }

		/// <summary>
		/// The main serializer class used for serializing and deserializing objects.
		/// This serializer leverages high-performance IL-based serialization techniques to enhance security and performance compared to traditional binary formatters.
		/// It manages a serialization context which includes tracking of already serialized objects to avoid duplication and a string pool for efficient string handling.
		/// </summary>
		public NeoBinarySerializer Serializer { get; set; }
	}
}