using System;

namespace CoreRemoting.DependencyInjection;

/// <summary>
/// Describes a registered service.
/// </summary>
public class ServiceRegistration
{
    /// <summary>
    /// Creates a new instance of the ServiceRegistration class.
    /// </summary>
    /// <param name="serviceName">Unique name of the service</param>
    /// <param name="interfaceType">Interface type used as remote interface</param>
    /// <param name="implementationType">Implementation type of the server (null, if a factory is used)</param>
    /// <param name="serviceLifetime">Service lifetime model</param>
    /// <param name="factory">Factory delegate that is used to create service instances</param>
    /// <param name="isHiddenSystemService">Specifies whether the registered service is a hidden system service or not</param>
    public ServiceRegistration(
        string serviceName,
        Type interfaceType,
        Type implementationType,
        ServiceLifetime serviceLifetime,
        Delegate factory,
        bool isHiddenSystemService)
    {
        if (factory != null && implementationType != null)
            throw new ArgumentException("ImplementationType could not be set, if a factory is used.",
                nameof(implementationType));
        
        if (factory == null && implementationType == null)
            throw new ArgumentException("ImplementationType must be specified, if no factory is used for instance creation.",
                nameof(implementationType));
        
        ServiceName = serviceName;
        InterfaceType = interfaceType;
        ImplementationType = implementationType;
        ServiceLifetime = serviceLifetime;
        Factory = factory;
        IsHiddenSystemService = isHiddenSystemService;
    }
    
    /// <summary>
    /// Returns the unique name of the service.
    /// </summary>
    public string ServiceName { get; }
    
    /// <summary>
    /// Returns the interface type that is used as remote interface.
    /// </summary>
    public Type InterfaceType { get; }
    
    /// <summary>
    /// Returns the implementation type (may be null, if a factory is used for instance creation).
    /// </summary>
    public Type ImplementationType { get; }
    
    /// <summary>
    /// Returns the service lifetime model.
    /// </summary>
    public ServiceLifetime ServiceLifetime { get; }

    /// <summary>
    /// Returns whether a factory is used for instance creation or not. 
    /// </summary>
    public bool UsesFactory => Factory != null;
    
    /// <summary>
    /// Returns the factory delegate used for instance created (may be null, if a fixed implementation type is specified).
    /// </summary>
    public Delegate Factory { get; }
    
    /// <summary>
    /// Returns whether the registered service is a hidden system service or not.
    /// </summary>
    public bool IsHiddenSystemService { get; }
}