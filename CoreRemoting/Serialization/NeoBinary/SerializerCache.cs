using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace CoreRemoting.Serialization.NeoBinary;

/// <summary>
/// High-performance cache for generated serializers with memory management and statistics.
/// </summary>
public partial class SerializerCache
{
	private readonly ConcurrentDictionary<Type, CachedSerializer> _serializerCache = new();
	private readonly ConcurrentDictionary<Type, CachedDeserializer> _deserializerCache = new();
	private readonly ConcurrentDictionary<Type, FieldInfo[]> _fieldCache = new();
	private readonly Timer _cleanupTimer;
	private readonly object _lockObject = new();
	private long _totalSerializations;
	private long _totalDeserializations;
	private long _cacheHits;
	private long _cacheMisses;

	/// <summary>
	/// Cached serializer with metadata.
	/// </summary>
	public class CachedSerializer
	{
		/// <summary>
		/// Serializer used for object serialization in the NeoBinary protocol.
		/// </summary>
		public IlTypeSerializer.ObjectSerializerDelegate Serializer { get; set; }

		/// <summary>
		/// Timestamp when the cached serializer was created.
		/// </summary>
		public DateTime CreatedAt { get; set; }

		/// <summary>
		/// Timestamp when the cached serializer was last accessed.
		/// </summary>
		public DateTime LastAccessed { get; set; }

		internal long _accessCount;
		private long _serializationCount;
		private long _totalSerializationTimeTicks;

		/// <summary>
		/// Number of times this serializer has been accessed.
		/// </summary>
		public long AccessCount => _accessCount;

		/// <summary>
		/// Number of serialization operations performed with this serializer.
		/// </summary>
		public long SerializationCount => _serializationCount;

		/// <summary>
		/// Total time spent in serialization operations for this serializer (in ticks).
		/// </summary>
		public long TotalSerializationTimeTicks => _totalSerializationTimeTicks;

		/// <summary>
		/// Records an access to this cached serializer.
		/// </summary>
		public void RecordAccess()
		{
			LastAccessed = DateTime.UtcNow;
			Interlocked.Increment(ref _accessCount);
		}

		/// <summary>
		/// Records a serialization operation with the elapsed time.
		/// </summary>
		/// <param name="elapsedTicks">Time elapsed for the serialization operation in ticks</param>
		public void RecordSerialization(long elapsedTicks)
		{
			Interlocked.Increment(ref _serializationCount);
			Interlocked.Add(ref _totalSerializationTimeTicks, elapsedTicks);
		}

		/// <summary>
		/// Average time spent in serialization operations for this serializer.
		/// </summary>
		[SuppressMessage("ReSharper", "UnusedMember.Global")]
		public TimeSpan AverageSerializationTime =>
			SerializationCount > 0 ? new TimeSpan(TotalSerializationTimeTicks / SerializationCount) : TimeSpan.Zero;
	}

	/// <summary>
	/// Gets the cache configuration.
	/// </summary>
	public CacheConfiguration Config { get; }

	/// <summary>
	/// Creates a new SerializerCache with default configuration.
	/// </summary>
	public SerializerCache() : this(new CacheConfiguration())
	{
	}

	/// <summary>
	/// Creates a new SerializerCache with specified configuration.
	/// </summary>
	/// <param name="config">Cache configuration</param>
	public SerializerCache(CacheConfiguration config)
	{
		Config = config ?? throw new ArgumentNullException(nameof(config));

		if (Config.EnableAutoCleanup)
			_cleanupTimer = new Timer(PerformCleanup, null,
				TimeSpan.FromSeconds(Config.CleanupIntervalSeconds),
				TimeSpan.FromSeconds(Config.CleanupIntervalSeconds));
	}

	/// <summary>
	/// Gets or creates a cached serializer for the specified type.
	/// </summary>
	/// <param name="type">Type to get serializer for</param>
	/// <param name="factory">Factory function to create serializer if not cached</param>
	/// <returns>Cached serializer</returns>
	public CachedSerializer GetOrCreateSerializer(Type type,
		Func<Type, IlTypeSerializer.ObjectSerializerDelegate> factory)
	{
		if (_serializerCache.TryGetValue(type, out var cached))
		{
			cached?.RecordAccess();
			Interlocked.Increment(ref _cacheHits);
			return cached;
		}

		lock (_lockObject)
		{
			// Double-check pattern
			if (_serializerCache.TryGetValue(type, out cached))
			{
				cached.RecordAccess();
				Interlocked.Increment(ref _cacheHits);
				return cached;
			}

			// Check cache size limit
			if (_serializerCache.Count >= Config.MaxCacheSize) EvictLeastUsedItems();

			var serializer = factory(type);
			cached = new CachedSerializer
			{
				Serializer = serializer,
				CreatedAt = DateTime.UtcNow,
				LastAccessed = DateTime.UtcNow
			};
			Interlocked.Increment(ref cached._accessCount);

			_serializerCache[type] = cached;
			Interlocked.Increment(ref _cacheMisses);
			return cached;
		}
	}

	/// <summary>
	/// Gets or creates a cached deserializer for the specified type.
	/// </summary>
	/// <param name="type">Type to get deserializer for</param>
	/// <param name="factory">Factory function to create deserializer if not cached</param>
	/// <returns>Cached deserializer</returns>
	public CachedDeserializer GetOrCreateDeserializer(Type type,
		Func<Type, IlTypeSerializer.ObjectDeserializerDelegate> factory)
	{
		if (_deserializerCache.TryGetValue(type, out var cached))
		{
			cached?.RecordAccess();
			Interlocked.Increment(ref _cacheHits);
			return cached;
		}

		lock (_lockObject)
		{
			// Double-check pattern
			if (_deserializerCache.TryGetValue(type, out cached))
			{
				cached.RecordAccess();
				Interlocked.Increment(ref _cacheHits);
				return cached;
			}

			// Check cache size limit
			if (_deserializerCache.Count >= Config.MaxCacheSize) EvictLeastUsedItems();

			var deserializer = factory(type);
			cached = new CachedDeserializer
			{
				Deserializer = deserializer,
				CreatedAt = DateTime.UtcNow,
				LastAccessed = DateTime.UtcNow
			};
			Interlocked.Increment(ref cached._accessCount);

			_deserializerCache[type] = cached;
			Interlocked.Increment(ref _cacheMisses);
			return cached;
		}
	}

	/// <summary>
	/// Gets or creates a cached compact serializer for the specified type.
	/// </summary>
	/// <param name="type">Type to get serializer for</param>
	/// <param name="factory">Factory function to create serializer if not cached</param>
	/// <returns>Cached serializer</returns>
	public CachedSerializer GetOrCreateCompactSerializer(Type type,
		Func<Type, IlTypeSerializer.ObjectSerializerDelegate> factory)
	{
		if (_serializerCache.TryGetValue(type, out var cached))
		{
			cached?.RecordAccess();
			Interlocked.Increment(ref _cacheHits);
			return cached;
		}

		lock (_lockObject)
		{
			// Double-check pattern
			if (_serializerCache.TryGetValue(type, out cached))
			{
				cached.RecordAccess();
				Interlocked.Increment(ref _cacheHits);
				return cached;
			}

			// Check cache size limit
			if (_serializerCache.Count >= Config.MaxCacheSize) EvictLeastUsedItems();

			var serializer = factory(type);
			cached = new CachedSerializer
			{
				Serializer = serializer,
				CreatedAt = DateTime.UtcNow,
				LastAccessed = DateTime.UtcNow
			};
			Interlocked.Increment(ref cached._accessCount);

			_serializerCache[type] = cached;
			Interlocked.Increment(ref _cacheMisses);
			return cached;
		}
	}

	/// <summary>
	/// Gets or creates a cached compact deserializer for the specified type.
	/// </summary>
	/// <param name="type">Type to get deserializer for</param>
	/// <param name="factory">Factory function to create deserializer if not cached</param>
	/// <returns>Cached deserializer</returns>
	public CachedDeserializer GetOrCreateCompactDeserializer(Type type,
		Func<Type, IlTypeSerializer.ObjectDeserializerDelegate> factory)
	{
		if (_deserializerCache.TryGetValue(type, out var cached))
		{
			cached?.RecordAccess();
			Interlocked.Increment(ref _cacheHits);
			return cached;
		}

		lock (_lockObject)
		{
			// Double-check pattern
			if (_deserializerCache.TryGetValue(type, out cached))
			{
				cached.RecordAccess();
				Interlocked.Increment(ref _cacheHits);
				return cached;
			}

			// Check cache size limit
			if (_deserializerCache.Count >= Config.MaxCacheSize) EvictLeastUsedItems();

			var deserializer = factory(type);
			cached = new CachedDeserializer
			{
				Deserializer = deserializer,
				CreatedAt = DateTime.UtcNow,
				LastAccessed = DateTime.UtcNow
			};
			Interlocked.Increment(ref cached._accessCount);

			_deserializerCache[type] = cached;
			Interlocked.Increment(ref _cacheMisses);
			return cached;
		}
	}

	/// <summary>
	/// Gets or creates cached field information for the specified type.
	/// </summary>
	/// <param name="type">Type to get fields for</param>
	/// <param name="factory">Factory function to get fields if not cached</param>
	/// <returns>Array of field information</returns>
	public FieldInfo[] GetOrCreateFields(Type type, Func<Type, FieldInfo[]> factory)
	{
		return _fieldCache.GetOrAdd(type, factory);
	}

	/// <summary>
	/// Gets or creates a pooled string.
	/// </summary>
	/// <param name="value">String value to pool</param>
	/// <returns>Pooled string instance</returns>
	public string GetOrCreatePooledString(string value)
	{
		return StringPool.GetOrAdd(value, v => v);
	}

	/// <summary>
	/// Gets the internal string pool for advanced usage.
	/// </summary>
	internal ConcurrentDictionary<string, string> StringPool { get; } = new();

	/// <summary>
	/// Records a serialization operation.
	/// </summary>
	public void RecordSerialization()
	{
		Interlocked.Increment(ref _totalSerializations);
	}

	/// <summary>
	/// Records a deserialization operation.
	/// </summary>
	public void RecordDeserialization()
	{
		Interlocked.Increment(ref _totalDeserializations);
	}

	/// <summary>
	/// Gets comprehensive cache statistics.
	/// </summary>
	/// <returns>Cache statistics</returns>
	public CacheStatistics GetStatistics()
	{
		lock (_lockObject)
		{
			return new CacheStatistics
			{
				SerializerCount = _serializerCache.Count,
				DeserializerCount = _deserializerCache.Count,
				FieldCacheCount = _fieldCache.Count,
				StringPoolCount = StringPool.Count,
				TotalSerializations = _totalSerializations,
				TotalDeserializations = _totalDeserializations,
				CacheHits = _cacheHits,
				CacheMisses = _cacheMisses,
				TopSerializers = _serializerCache
					.OrderByDescending(kvp => kvp.Value.AccessCount)
					.Take(10)
					.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
				TopDeserializers = _deserializerCache
					.OrderByDescending(kvp => kvp.Value.AccessCount)
					.Take(10)
					.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
			};
		}
	}

	/// <summary>
	/// Clears all cached items.
	/// </summary>
	public void Clear()
	{
		lock (_lockObject)
		{
			_serializerCache.Clear();
			_deserializerCache.Clear();
			_fieldCache.Clear();
			StringPool.Clear();

			// Reset statistics
			Interlocked.Exchange(ref _totalSerializations, 0);
			Interlocked.Exchange(ref _totalDeserializations, 0);
			Interlocked.Exchange(ref _cacheHits, 0);
			Interlocked.Exchange(ref _cacheMisses, 0);
		}
	}

	/// <summary>
	/// Performs cleanup of old and unused cache items.
	/// </summary>
	/// <param name="state">Timer state (unused)</param>
	private void PerformCleanup(object state)
	{
		if (!Config.EnableAutoCleanup)
			return;

		try
		{
			lock (_lockObject)
			{
				var now = DateTime.UtcNow;
				var maxAge = TimeSpan.FromMinutes(Config.MaxCacheAgeMinutes);

				// Clean up serializers
				var serializersToRemove = _serializerCache
					.Where(kvp =>
						kvp.Value.AccessCount < Config.MinAccessCount ||
						now - kvp.Value.LastAccessed > maxAge)
					.Select(kvp => kvp.Key)
					.ToList();

				foreach (var key in serializersToRemove) _serializerCache.TryRemove(key, out _);

				// Clean up deserializers
				var deserializersToRemove = _deserializerCache
					.Where(kvp =>
						kvp.Value.AccessCount < Config.MinAccessCount ||
						now - kvp.Value.LastAccessed > maxAge)
					.Select(kvp => kvp.Key)
					.ToList();

				foreach (var key in deserializersToRemove) _deserializerCache.TryRemove(key, out _);

				// Clean up string pool (keep only frequently used strings)
				if (StringPool.Count > Config.MaxCacheSize / 2)
				{
					// This is a simple cleanup strategy - in practice, string pool cleanup
					// might need more sophisticated logic based on usage patterns
					var stringsToRemove = StringPool.Keys
						.Take(Math.Max(0, StringPool.Count - Config.MaxCacheSize / 2))
						.ToList();

					foreach (var key in stringsToRemove) StringPool.TryRemove(key, out _);
				}
			}
		}
		catch
		{
			// Ignore cleanup errors to avoid disrupting serialization
		}
	}

	/// <summary>
	/// Evicts least used items when cache is full using LFU/LRU hybrid scoring.
	/// </summary>
	private void EvictLeastUsedItems()
	{
		// Remove least used serializers
		var serializersToRemove = _serializerCache
			.OrderByDescending(kvp => CalculateEvictionScore(kvp.Value))
			.Take(Math.Max(1, _serializerCache.Count / 10))
			.Select(kvp => kvp.Key)
			.ToList();

		foreach (var key in serializersToRemove) _serializerCache.TryRemove(key, out _);

		// Remove least used deserializers
		var deserializersToRemove = _deserializerCache
			.OrderByDescending(kvp => CalculateEvictionScore(kvp.Value))
			.Take(Math.Max(1, _deserializerCache.Count / 10))
			.Select(kvp => kvp.Key)
			.ToList();

		foreach (var key in deserializersToRemove) _deserializerCache.TryRemove(key, out _);
	}

	/// <summary>
	/// Calculates an eviction score for serializer cache items (higher = more likely to be evicted).
	/// Combines recency (LRU) and frequency (LFU) factors.
	/// </summary>
	/// <param name="cachedItem">The cached serializer to score</param>
	/// <returns>Eviction score (higher = evict first)</returns>
	private double CalculateEvictionScore(CachedSerializer cachedItem)
	{
		// Recency factor: How long since last access (normalized to 0-1)
		var timeSinceAccess = (DateTime.UtcNow - cachedItem.LastAccessed).TotalMinutes;
		var recencyScore = Math.Min(timeSinceAccess / Config.MaxCacheAgeMinutes, 1.0);

		// Frequency factor: Inverse of access count (lower access = higher score)
		var frequencyScore = 1.0 / (cachedItem.AccessCount + 1.0); // +1 prevents division by zero

		// Weighted combination: 70% recency, 30% frequency
		return recencyScore * 0.7 + frequencyScore * 0.3;
	}

	/// <summary>
	/// Calculates an eviction score for deserializer cache items (higher = more likely to be evicted).
	/// Combines recency (LRU) and frequency (LFU) factors.
	/// </summary>
	/// <param name="cachedItem">The cached deserializer to score</param>
	/// <returns>Eviction score (higher = evict first)</returns>
	private double CalculateEvictionScore(CachedDeserializer cachedItem)
	{
		// Recency factor: How long since last access (normalized to 0-1)
		var timeSinceAccess = (DateTime.UtcNow - cachedItem.LastAccessed).TotalMinutes;
		var recencyScore = Math.Min(timeSinceAccess / Config.MaxCacheAgeMinutes, 1.0);

		// Frequency factor: Inverse of access count (lower access = higher score)
		var frequencyScore = 1.0 / (cachedItem.AccessCount + 1.0); // +1 prevents division by zero

		// Weighted combination: 70% recency, 30% frequency
		return recencyScore * 0.7 + frequencyScore * 0.3;
	}

	/// <summary>
	/// Disposes the cache and cleanup timer.
	/// </summary>
	public void Dispose()
	{
		_cleanupTimer?.Dispose();
		Clear();
	}
}