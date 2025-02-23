using Castle.DynamicProxy;

namespace CoreRemoting;

/// <summary>
/// Default remoting proxy builder implementation.
/// </summary>
public class RemotingProxyBuilder
{
    /// <summary>
    /// Gets the proxy generator instance.
    /// </summary>
    protected static readonly ProxyGenerator ProxyGenerator = new();

    /// <summary>
    /// Creates a proxy object to provide access to a remote service.
    /// </summary>
    /// <typeparam name="T">Type of the shared interface of the remote service</typeparam>
    /// <param name="remotingClient"><see cref="IRemotingClient"/> instance to make remote calls</param>
    /// <param name="serviceName">Unique name of the remote service</param>
    /// <returns>Proxy object</returns>
    public virtual T CreateProxy<T>(RemotingClient remotingClient, string serviceName = "") =>
        (T)ProxyGenerator.CreateInterfaceProxyWithoutTarget(
            interfaceToProxy: typeof(T),
            interceptor: new ServiceProxy<T>(
                client: remotingClient,
                serviceName: serviceName));
}
