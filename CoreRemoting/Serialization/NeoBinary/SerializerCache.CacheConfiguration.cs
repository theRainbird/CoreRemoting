namespace CoreRemoting.Serialization.NeoBinary;

public partial class SerializerCache
{
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
}