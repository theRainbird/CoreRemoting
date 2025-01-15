using System;
using System.Collections.Generic;
using System.Threading;
using CoreRemoting.DependencyInjection;
using CoreRemoting.Toolbox;
using DryIoc;
using DryIoc.MefAttributedModel;

namespace CoreRemoting.Tests.Tools;

/// <summary>
/// Dependency injection container based on DryIoc container implementation.
/// </summary>
public class DryIocContainerAdapter : DependencyInjectionContainerBase, IDependencyInjectionContainer
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DryIocAdapter"/> class.
    /// </summary>
    /// <param name="container">Optional container instance.</param>
    public DryIocContainerAdapter(IContainer container = null) =>
        RootContainer = container ?? new Container().WithMef()
            .With(rules => rules
                .With(FactoryMethod.ConstructorWithResolvableArguments)
                .WithCaptureContainerDisposeStackTrace()
                .WithoutThrowIfDependencyHasShorterReuseLifespan()
                .WithDefaultReuse(Reuse.ScopedOrSingleton));

    private IContainer RootContainer { get; }

    public override void Dispose()
    {
        base.Dispose();
        RootContainer.Dispose();
    }

    public override IEnumerable<Type> GetAllRegisteredTypes() => []; // TODO

    public override bool IsRegistered<TServiceInterface>(string serviceName = "") =>
        RootContainer.IsRegistered<TServiceInterface>(serviceName);

    private static object GetKey<TServiceInterface>(string serviceName) =>
        GetKey(typeof(TServiceInterface), serviceName);

    private static object GetKey(Type serviceInterface, string serviceName) =>
        string.IsNullOrWhiteSpace(serviceName) || serviceInterface.FullName.Equals(serviceName) ?
            null : serviceName;

    private static IReuse GetReuse(ServiceLifetime lifetime) => lifetime switch
    {
        ServiceLifetime.SingleCall => Reuse.Transient,
        ServiceLifetime.Singleton => Reuse.Singleton,
        ServiceLifetime.Scoped => Reuse.ScopedOrSingleton,
        _ => throw new NotSupportedException($"Lifetime not supported: {lifetime}."),
    };

    protected override void RegisterServiceInContainer<TServiceInterface, TServiceImpl>(ServiceLifetime lifetime, string serviceName = "") =>
        RootContainer.Register<TServiceInterface, TServiceImpl>(GetReuse(lifetime), serviceKey: GetKey<TServiceInterface>(serviceName));

    protected override void RegisterServiceInContainer<TServiceInterface>(Func<TServiceInterface> factoryDelegate, ServiceLifetime lifetime, string serviceName = "") =>
        RootContainer.RegisterDelegate(factoryDelegate, GetReuse(lifetime), serviceKey: GetKey<TServiceInterface>(serviceName));

    protected override object ResolveServiceFromContainer(ServiceRegistration registration) =>
        Container.Resolve(registration.InterfaceType ?? registration.ImplementationType,
            serviceKey: GetKey(registration.InterfaceType ?? registration.ImplementationType, registration.ServiceName));

    protected override TServiceInterface ResolveServiceFromContainer<TServiceInterface>(ServiceRegistration registration) =>
        ResolveServiceFromContainer(registration) as TServiceInterface;

    private IResolverContext Container => Scope.Value ?? RootContainer;

    private static AsyncLocal<IResolverContext> Scope { get; } = new();

    public override IDisposable CreateScope()
    {
        var oldValue = Scope.Value;
        Scope.Value = RootContainer.OpenScope();
        return Disposable.Create(() => Scope.Value = oldValue);
    }
}
