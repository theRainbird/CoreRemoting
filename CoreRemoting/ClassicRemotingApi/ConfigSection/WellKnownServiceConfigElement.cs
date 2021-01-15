using System;
using System.Configuration;
using CoreRemoting.DependencyInjection;

namespace CoreRemoting.ClassicRemotingApi.ConfigSection
{
    public class WellKnownServiceConfigElement : ConfigurationElement
    {
        [ConfigurationProperty("interfaceAssemblyName", IsRequired = true)]
        public string InterfaceAssemblyName
        {
            get => (string)base["interfaceAssemblyName"];
            set => base["interfaceAssemblyName"] = value;
        }
        
        [ConfigurationProperty("interfaceTypeName", IsRequired = true)]
        public string InterfaceTypeName
        {
            get => (string)base["interfaceTypeName"];
            set => base["interfaceTypeName"] = value;
        }
        
        [ConfigurationProperty("implementationAssemblyName", IsRequired = true)]
        public string ImplementationAssemblyName
        {
            get => (string)base["implementationAssemblyName"];
            set => base["implementationAssemblyName"] = value;
        }
        
        [ConfigurationProperty("implementationTypeName", IsRequired = true)]
        public string ImplementationTypeName
        {
            get => (string)base["implementationTypeName"];
            set => base["implementationTypeName"] = value;
        }
        
        [ConfigurationProperty("lifetime", IsRequired = true)]
        public ServiceLifetime Lifetime
        {
            get => (ServiceLifetime)base["lifetime"];
            set => base["lifetime"] = value;
        }
        
        [ConfigurationProperty("serviceName", IsRequired = true, IsKey = true)]
        public string ServiceName
        {
            get => (string)base["serviceName"];
            set => base["serviceName"] = value;
        }
        
        [ConfigurationProperty("uniqueInstanceName", IsRequired = false, DefaultValue = "")]
        public string UniqueInstanceName
        {
            get => (string)base["uniqueInstanceName"];
            set => base["uniqueInstanceName"] = value;
        }
    }
}