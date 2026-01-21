using System;
using System.Threading;

namespace CoreRemoting.Serialization.NeoBinary;

public partial class SerializerCache
{
	/// <summary>
	/// Cached deserializer with metadata.
	/// </summary>
	public class CachedDeserializer
	{
		/// <summary>
		/// Deserializer used for object deserialization in the NeoBinary protocol.
		/// </summary>
		public IlTypeSerializer.ObjectDeserializerDelegate Deserializer { get; set; }

		/// <summary>
		/// Timestamp when the cached deserializer was created.
		/// </summary>
		public DateTime CreatedAt { get; set; }

		/// <summary>
		/// Timestamp when the cached deserializer was last accessed.
		/// </summary>
		public DateTime LastAccessed { get; set; }

		internal long _accessCount;
		private long _deserializationCount;
		private long _totalDeserializationTimeTicks;

		/// <summary>
		/// Number of times this deserializer has been accessed.
		/// </summary>
		public long AccessCount => _accessCount;

		/// <summary>
		/// Records an access to this cached deserializer.
		/// </summary>
		public void RecordAccess()
		{
			LastAccessed = DateTime.UtcNow;
			Interlocked.Increment(ref _accessCount);
		}

		/// <summary>
		/// Records a deserialization operation with the elapsed time.
		/// </summary>
		/// <param name="elapsedTicks">Time elapsed for the deserialization operation in ticks</param>
		public void RecordDeserialization(long elapsedTicks)
		{
			Interlocked.Increment(ref _deserializationCount);
			Interlocked.Add(ref _totalDeserializationTimeTicks, elapsedTicks);
		}
	}
}