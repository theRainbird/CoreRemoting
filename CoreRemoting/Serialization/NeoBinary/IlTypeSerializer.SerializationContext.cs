using System.Collections.Concurrent;
using System.Collections.Generic;

namespace CoreRemoting.Serialization.NeoBinary;

public partial class IlTypeSerializer
{
	/// <summary>
	/// Serialization context containing shared state.
	/// </summary>
	public class SerializationContext
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

		/// <summary>
		/// A pool of reusable string instances to optimize memory usage and improve performance.
		/// The string pool helps in reducing the memory footprint by reusing string objects that are likely to be duplicated throughout the application.
		/// </summary>
		public ConcurrentDictionary<string, string> StringPool { get; set; }
	}
}