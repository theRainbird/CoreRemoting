using System;

namespace CoreRemoting.Tests.Tools;

public interface IScopedService
{
    Guid InstanceId { get; }
}
