using System;

namespace CoreRemoting.Serialization
{
    /// <summary>
    /// Describes a reference to a service that can be passed to a remote receiver.
    /// </summary>
    [Serializable]
    public class ServiceReference
    {
        /// <summary>
        /// Creates a new instance of the ServiceReference class.
        /// </summary>
        /// <param name="serviceInterfaceTypeName">interface type name of the referenced service (e.g. "SomeNamespace.ISomeService, SomeAssembly")</param>
        /// <param name="serviceName">name of the referenced service</param>
        public ServiceReference(string serviceInterfaceTypeName, string serviceName)
        {
            ServiceInterfaceTypeName = serviceInterfaceTypeName;
            ServiceName = serviceName;
        }
        
        /// <summary>
        /// Gets the interface type name of the referenced service (e.g. "SomeNamespace.ISomeService, SomeAssembly")
        /// </summary>
        public string ServiceInterfaceTypeName { get; }
        
        /// <summary>
        /// Gets the name of the referenced service.
        /// </summary>
        public string ServiceName { get; }
    }
}