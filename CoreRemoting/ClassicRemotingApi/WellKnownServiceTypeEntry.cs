using CoreRemoting.DependencyInjection;

namespace CoreRemoting.ClassicRemotingApi
{
    /// <summary>
    /// Describes a wellknown service.
    /// </summary>
    public class WellKnownServiceTypeEntry
    {
        /// <summary>
        /// Creates a new instance of the WellKnownServiceTypeEntry class.
        /// </summary>
        /// <param name="interfaceAssemblyName">Interface assembly name of the service</param>
        /// <param name="interfaceTypeName">Interface type name of the service</param>
        /// <param name="implementationAssemblyName">Implementation assembly name of the service</param>
        /// <param name="implementationTypeName">Implementation type name of the service</param>
        /// <param name="lifetime">Lifetime of the service (Singleton / SingleCall)</param>
        /// <param name="serviceName">Unique service name (Full name of the interface type is used, when left blank)</param>
        /// <param name="uniqueServerInstanceName">Unique instance name of the host server (default server is used, if left blank)</param>
        public WellKnownServiceTypeEntry(
            string interfaceAssemblyName, 
            string interfaceTypeName,
            string implementationAssemblyName,
            string implementationTypeName,
            ServiceLifetime lifetime,
            string serviceName = "",
            string uniqueServerInstanceName = "")
        {
            ImplementationAssemblyName = implementationAssemblyName;
            ImplementationTypeName = implementationTypeName;
            InterfaceAssemblyName = interfaceAssemblyName;
            InterfaceTypeName = interfaceTypeName;
            Lifetime = lifetime;
            ServiceName = serviceName;
            UniqueServerInstanceName = uniqueServerInstanceName;
        }
        
        /// <summary>
        /// Gets or sets the unique instance name of the host server.
        /// </summary>
        public string UniqueServerInstanceName { get; set; }
        
        /// <summary>
        /// Gets or sets the implementation assembly name.
        /// </summary>
        public string ImplementationAssemblyName { get; set; }
        
        /// <summary>
        /// Gets or sets the implementation type name.
        /// </summary>
        public string ImplementationTypeName { get; set; }
        
        /// <summary>
        /// Gets or sets the interface assembly name.
        /// </summary>
        public string InterfaceAssemblyName { get; set; }
        
        /// <summary>
        /// Gets or sets the interface type name.
        /// </summary>
        public string InterfaceTypeName { get; set; }
        
        /// <summary>
        /// Gets or sets the service's lifetime.
        /// </summary>
        public ServiceLifetime Lifetime { get; set; }
        
        /// <summary>
        /// Gets or sets the unique service name (Full name of interface type is used, if left blank).
        /// </summary>
        public string ServiceName { get; set; }
    }
}