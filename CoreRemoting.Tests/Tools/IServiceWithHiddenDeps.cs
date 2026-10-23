using System;

namespace CoreRemoting.Tests.Tools;

public interface IServiceWithHiddenDeps
{
    bool IsImported { get; }
}
