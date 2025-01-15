using System;

namespace CoreRemoting.Tests.Tools;

public interface IServiceWithDeps
{
    Guid ScopedServiceInstanceId { get; }
}
