using CoreRemoting.DependencyInjection;

namespace CoreRemoting.ClassicRemotingApi
{
    public class WellKnownServiceTypeEntry
    {
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
        
        public string UniqueServerInstanceName { get; set; }
        
        public string ImplementationAssemblyName { get; set; }
        
        public string ImplementationTypeName { get; set; }
        
        public string InterfaceAssemblyName { get; set; }
        
        public string InterfaceTypeName { get; set; }
        
        public ServiceLifetime Lifetime { get; set; }
        
        public string ServiceName { get; set; }
    }
}