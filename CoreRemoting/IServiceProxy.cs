namespace CoreRemoting
{
    /// <summary>
    /// Interface to be implemented by service proxy classes.
    /// </summary>
    public interface IServiceProxy
    {
        /// <summary>
        /// Shuts the proxy object down.
        /// This is called from proxy objects finalizer because the proxy mimics its proxied interface. 
        /// </summary>
        void Shutdown();
    }
}