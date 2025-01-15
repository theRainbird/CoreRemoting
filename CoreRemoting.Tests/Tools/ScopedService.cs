using System;

namespace CoreRemoting.Tests.Tools;

public class ScopedService : IScopedService
{
    public Guid InstanceId { get; } = Guid.NewGuid();
}
