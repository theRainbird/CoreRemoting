using System.Configuration;
using CoreRemoting.DependencyInjection;

namespace CoreRemoting.ClassicRemotingApi.ConfigSection
{
    /// <summary>
    /// Configuration element for a wellknown CoreRemoting service.
    /// </summary>
    public class WellKnownServiceConfigElement : ConfigurationElement
    {
        /// <summary>
        /// Get or sets the assembly name of the service’s interface assembly.
        /// </summary>
        [ConfigurationProperty("interfaceAssemblyName", IsRequired = true)]
        public string InterfaceAssemblyName
        {
            get => (string)base["interfaceAssemblyName"];
            set => base["interfaceAssemblyName"] = value;
        }
        
        /// <summary>
        /// Get or sets the type name of the service interface.
        /// </summary>
        [ConfigurationProperty("interfaceTypeName", IsRequired = true)]
        public string InterfaceTypeName
        {
            get => (string)base["interfaceTypeName"];
            set => base["interfaceTypeName"] = value;
        }
        
        /// <summary>
        /// Get or sets the assembly of the service's implementation assembly. 
        /// </summary>
        [ConfigurationProperty("implementationAssemblyName", IsRequired = true)]
        public string ImplementationAssemblyName
        {
            get => (string)base["implementationAssemblyName"];
            set => base["implementationAssemblyName"] = value;
        }
        
        /// <summary>
        /// Gets or sets the implementation type of the service.
        /// </summary>
        [ConfigurationProperty("implementationTypeName", IsRequired = true)]
        public string ImplementationTypeName
        {
            get => (string)base["implementationTypeName"];
            set => base["implementationTypeName"] = value;
        }
        
        /// <summary>
        /// Gets or sets the service's lifetime.
        /// </summary>
        [ConfigurationProperty("lifetime", IsRequired = true)]
        public ServiceLifetime Lifetime
        {
            get => (ServiceLifetime)base["lifetime"];
            set => base["lifetime"] = value;
        }
        
        /// <summary>
        /// Gets or sets the unique service name.
        /// </summary>
        [ConfigurationProperty("serviceName", IsRequired = true, IsKey = true)]
        public string ServiceName
        {
            get => (string)base["serviceName"];
            set => base["serviceName"] = value;
        }
        
        /// <summary>
        /// Gets or sets the unique name of the server hosting this service.
        /// </summary>
        [ConfigurationProperty("uniqueServerInstanceName", IsRequired = false, DefaultValue = "")]
        public string UniqueServerInstanceName
        {
            get => (string)base["uniqueServerInstanceName"];
            set => base["uniqueServerInstanceName"] = value;
        }
    }
}