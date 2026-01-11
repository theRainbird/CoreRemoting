using System;
using System.Collections.Generic;

namespace CoreRemoting.Serialization.NeoBinary;

public partial class NeoBinarySerializer
{
	/// <summary>
	/// Provides thread-safe isolation for type reference tables during (de)serialization.
	/// This prevents race conditions when multiple threads use the same serializer instance.
	/// </summary>
	internal class SerializationContext
	{
		/// <summary>
		/// Type table for the current serialization operation.
		/// </summary>
		public List<Type> TypeTable { get; } = new(64);

		/// <summary>
		/// Type key to ID mapping for efficient type references.
		/// Key format: typeName|assemblyName|version
		/// </summary>
		public Dictionary<string, int> TypeKeyToId { get; } = new(128);

		/// <summary>
		/// Whether type references are active for this operation.
		/// </summary>
		public bool TypeRefActive { get; set; }
	}
}