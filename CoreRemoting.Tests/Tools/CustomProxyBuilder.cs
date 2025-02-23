namespace CoreRemoting.Tests.Tools;

/// <summary>
/// Custom proxy builder for unit tests.
/// </summary>
public class CustomProxyBuilder : RemotingProxyBuilder
{
    /// <summary>
    /// Intercepts <see cref="IGenericEchoService"/> calls.
    /// </summary>
    private class GenericEchoInterceptor : IGenericEchoService
    {
        public GenericEchoInterceptor(IGenericEchoService svc)
        {
            Svc = svc;
        }

        public IGenericEchoService Svc { get; }

        public T Echo<T>(T value)
        {
            var result = Svc.Echo(value);
            if (value is string v)
            {
                v = "[" + v + "]";
                return (T)(object)v;
            }

            return result;
        }
    }

    public override T CreateProxy<T>(RemotingClient remotingClient, string serviceName = "")
    {
        var proxy = base.CreateProxy<T>(remotingClient, serviceName);
        if (typeof(T) != typeof(IGenericEchoService))
        {
            return proxy;
        }

        var result = new GenericEchoInterceptor(proxy as IGenericEchoService);
        return (T)(object)result;
    }
}
