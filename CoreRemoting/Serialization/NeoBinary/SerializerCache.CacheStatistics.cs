using System;
using System.Collections.Generic;

namespace CoreRemoting.Serialization.NeoBinary;

public partial class SerializerCache
{
	/// <summary>
	/// Cache statistics.
	/// </summary>
	public class CacheStatistics
	{
		/// <summary>
		/// Number of cached serializers.
		/// </summary>
		public int SerializerCount { get; set; }

		/// <summary>
		/// Number of cached deserializers.
		/// </summary>
		public int DeserializerCount { get; set; }

		/// <summary>
		/// Number of cached field information entries.
		/// </summary>
		public int FieldCacheCount { get; set; }

		/// <summary>
		/// Number of strings in the string pool.
		/// </summary>
		public int StringPoolCount { get; set; }

		/// <summary>
		/// Total number of serialization operations performed.
		/// </summary>
		public long TotalSerializations { get; set; }

		/// <summary>
		/// Total number of deserialization operations performed.
		/// </summary>
		public long TotalDeserializations { get; set; }

		/// <summary>
		/// Total number of cache hits.
		/// </summary>
		public long CacheHits { get; set; }

		/// <summary>
		/// Total number of cache misses.
		/// </summary>
		public long CacheMisses { get; set; }

		/// <summary>
		/// Cache hit ratio (0.0 to 1.0).
		/// </summary>
		public double HitRatio => TotalSerializations + TotalDeserializations > 0
			? (double)CacheHits / (TotalSerializations + TotalDeserializations)
			: 0.0;

		/// <summary>
		/// Top 10 most accessed serializers by type.
		/// </summary>
		public Dictionary<Type, CachedSerializer> TopSerializers { get; set; } = new();

		/// <summary>
		/// Top 10 most accessed deserializers by type.
		/// </summary>
		public Dictionary<Type, CachedDeserializer> TopDeserializers { get; set; } = new();
	}
}