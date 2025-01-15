using CoreRemoting.DependencyInjection;

namespace CoreRemoting.Tests;

public class DicTests_Microsoft : DicTests
{
    public override IDependencyInjectionContainer Container =>
        new MicrosoftDependencyInjectionContainer();

    protected override bool SupportsNamedServices => false;
}
