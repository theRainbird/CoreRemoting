using System;

namespace CoreRemoting.Tests.Tools;

public class ServiceWithDeps : IServiceWithDeps
{
    public ServiceWithDeps(IAsyncService async, IScopedService scoped1, IScopedService scoped2)
    {
        AsyncService = async;
        ScopedService1 = scoped1;
        ScopedService2 = scoped2;
    }

    public IAsyncService AsyncService { get; }

    public IScopedService ScopedService1 { get; }

    public IScopedService ScopedService2 { get; }

    public Guid ScopedServiceInstanceId
    {
        get
        {
            if (ScopedService1.InstanceId != ScopedService2.InstanceId)
                return Guid.Empty;

            return ScopedService1.InstanceId;
        }
    }
}
