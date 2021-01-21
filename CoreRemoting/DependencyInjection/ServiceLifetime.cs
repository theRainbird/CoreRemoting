namespace CoreRemoting.DependencyInjection
{
    /// <summary>
    /// Describes the available service lifetime modes.
    /// </summary>
    public enum ServiceLifetime
    {
        /// <summary>
        /// On service instance serves all calls.
        /// </summary>
        Singleton = 1,
        /// <summary>
        /// Every call is served by its own service instance.
        /// </summary>
        SingleCall = 2
    }
}