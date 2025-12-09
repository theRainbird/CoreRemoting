using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace CoreRemoting.Serialization.NeoBinary
{
	/// <summary>
	/// High-performance cache for generated serializers with memory management and statistics.
	/// </summary>
	public class SerializerCache
	{
		private readonly ConcurrentDictionary<Type, CachedSerializer> _serializerCache = new();
		private readonly ConcurrentDictionary<Type, CachedDeserializer> _deserializerCache = new();
		private readonly ConcurrentDictionary<Type, FieldInfo[]> _fieldCache = new();
		private readonly ConcurrentDictionary<string, string> _stringPool = new();
		private readonly Timer _cleanupTimer;
		private readonly object _lockObject = new object();
		private long _totalSerializations;
		private long _totalDeserializations;
		private long _cacheHits;
		private long _cacheMisses;

		/// <summary>
		/// Configuration for cache behavior.
		/// </summary>
		public class CacheConfiguration
		{
			/// <summary>
			/// Maximum number of cached serializers (default: 1000).
			/// </summary>
			public int MaxCacheSize { get; set; } = 1000;

			/// <summary>
			/// Cleanup interval in seconds (default: 300 = 5 minutes).
			/// </summary>
			public int CleanupIntervalSeconds { get; set; } = 300;

			/// <summary>
			/// Minimum access count to keep in cache during cleanup (default: 10).
			/// </summary>
			public int MinAccessCount { get; set; } = 10;

			/// <summary>
			/// Maximum age of cached items in minutes (default: 60 minutes).
			/// </summary>
			public int MaxCacheAgeMinutes { get; set; } = 60;

			/// <summary>
			/// Whether to enable automatic cleanup (default: true).
			/// </summary>
			public bool EnableAutoCleanup { get; set; } = true;
		}

		/// <summary>
		/// Cached serializer with metadata.
		/// </summary>
		public class CachedSerializer
		{
			public IlTypeSerializer.ObjectSerializerDelegate Serializer { get; set; }
			public DateTime CreatedAt { get; set; }
			public DateTime LastAccessed { get; set; }
			internal long _accessCount;
			internal long _serializationCount;
			internal long _totalSerializationTimeTicks;

			public long AccessCount => _accessCount;
			public long SerializationCount => _serializationCount;
			public long TotalSerializationTimeTicks => _totalSerializationTimeTicks;

			public void RecordAccess()
			{
				LastAccessed = DateTime.UtcNow;
				Interlocked.Increment(ref _accessCount);
			}

			public void RecordSerialization(long elapsedTicks)
			{
				Interlocked.Increment(ref _serializationCount);
				Interlocked.Add(ref _totalSerializationTimeTicks, elapsedTicks);
			}

			public TimeSpan AverageSerializationTime =>
				SerializationCount > 0 ? new TimeSpan(TotalSerializationTimeTicks / SerializationCount) : TimeSpan.Zero;
		}

		/// <summary>
		/// Cached deserializer with metadata.
		/// </summary>
		public class CachedDeserializer
		{
			public IlTypeSerializer.ObjectDeserializerDelegate Deserializer { get; set; }
			public DateTime CreatedAt { get; set; }
			public DateTime LastAccessed { get; set; }
			internal long _accessCount;
			internal long _deserializationCount;
			internal long _totalDeserializationTimeTicks;

			public long AccessCount => _accessCount;
			public long DeserializationCount => _deserializationCount;
			public long TotalDeserializationTimeTicks => _totalDeserializationTimeTicks;

			public void RecordAccess()
			{
				LastAccessed = DateTime.UtcNow;
				Interlocked.Increment(ref _accessCount);
			}

			public void RecordDeserialization(long elapsedTicks)
			{
				Interlocked.Increment(ref _deserializationCount);
				Interlocked.Add(ref _totalDeserializationTimeTicks, elapsedTicks);
			}

			public TimeSpan AverageDeserializationTime =>
				DeserializationCount > 0
					? new TimeSpan(TotalDeserializationTimeTicks / DeserializationCount)
					: TimeSpan.Zero;
		}

		/// <summary>
		/// Cache statistics.
		/// </summary>
		public class CacheStatistics
		{
			public int SerializerCount { get; set; }
			public int DeserializerCount { get; set; }
			public int FieldCacheCount { get; set; }
			public int StringPoolCount { get; set; }
			public long TotalSerializations { get; set; }
			public long TotalDeserializations { get; set; }
			public long CacheHits { get; set; }
			public long CacheMisses { get; set; }

			public double HitRatio => TotalSerializations + TotalDeserializations > 0
				? (double)CacheHits / (TotalSerializations + TotalDeserializations)
				: 0.0;

			public Dictionary<Type, CachedSerializer> TopSerializers { get; set; } = new();
			public Dictionary<Type, CachedDeserializer> TopDeserializers { get; set; } = new();
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
			{
				_cleanupTimer = new Timer(PerformCleanup, null,
					TimeSpan.FromSeconds(Config.CleanupIntervalSeconds),
					TimeSpan.FromSeconds(Config.CleanupIntervalSeconds));
			}
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
				cached.RecordAccess();
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
				if (_serializerCache.Count >= Config.MaxCacheSize)
				{
					EvictLeastUsedItems();
				}

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
				cached.RecordAccess();
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
				if (_deserializerCache.Count >= Config.MaxCacheSize)
				{
					EvictLeastUsedItems();
				}

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
			return _stringPool.GetOrAdd(value, v => v);
		}

		/// <summary>
		/// Gets the internal string pool for advanced usage.
		/// </summary>
		internal ConcurrentDictionary<string, string> StringPool => _stringPool;

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
			return new CacheStatistics
			{
				SerializerCount = _serializerCache.Count,
				DeserializerCount = _deserializerCache.Count,
				FieldCacheCount = _fieldCache.Count,
				StringPoolCount = _stringPool.Count,
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
				_stringPool.Clear();

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

					foreach (var key in serializersToRemove)
					{
						_serializerCache.TryRemove(key, out _);
					}

					// Clean up deserializers
					var deserializersToRemove = _deserializerCache
						.Where(kvp =>
							kvp.Value.AccessCount < Config.MinAccessCount ||
							now - kvp.Value.LastAccessed > maxAge)
						.Select(kvp => kvp.Key)
						.ToList();

					foreach (var key in deserializersToRemove)
					{
						_deserializerCache.TryRemove(key, out _);
					}

					// Clean up string pool (keep only frequently used strings)
					if (_stringPool.Count > Config.MaxCacheSize / 2)
					{
						// This is a simple cleanup strategy - in practice, string pool cleanup
						// might need more sophisticated logic based on usage patterns
						var stringsToRemove = _stringPool.Keys
							.Take(Math.Max(0, _stringPool.Count - Config.MaxCacheSize / 2))
							.ToList();

						foreach (var key in stringsToRemove)
						{
							_stringPool.TryRemove(key, out _);
						}
					}
				}
			}
			catch
			{
				// Ignore cleanup errors to avoid disrupting serialization
			}
		}

		/// <summary>
		/// Evicts least used items when cache is full.
		/// </summary>
		private void EvictLeastUsedItems()
		{
			// Remove least used serializers
			var serializersToRemove = _serializerCache
				.OrderBy(kvp => kvp.Value.AccessCount)
				.Take(Math.Max(1, _serializerCache.Count / 10))
				.Select(kvp => kvp.Key)
				.ToList();

			foreach (var key in serializersToRemove)
			{
				_serializerCache.TryRemove(key, out _);
			}

			// Remove least used deserializers
			var deserializersToRemove = _deserializerCache
				.OrderBy(kvp => kvp.Value.AccessCount)
				.Take(Math.Max(1, _deserializerCache.Count / 10))
				.Select(kvp => kvp.Key)
				.ToList();

			foreach (var key in deserializersToRemove)
			{
				_deserializerCache.TryRemove(key, out _);
			}
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
}