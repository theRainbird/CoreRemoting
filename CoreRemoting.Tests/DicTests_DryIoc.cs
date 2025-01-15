using CoreRemoting.DependencyInjection;
using CoreRemoting.Tests.Tools;

namespace CoreRemoting.Tests;

public class DicTests_DryIoc : DicTests
{
    public override IDependencyInjectionContainer Container =>
        new DryIocContainerAdapter();

    protected override bool SupportsNamedServices => true;
}
