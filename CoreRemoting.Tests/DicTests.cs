﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreRemoting.DependencyInjection;
using CoreRemoting.Tests.Tools;
using Xunit;

namespace CoreRemoting.Tests;

public class DicTests
{
    public virtual IDependencyInjectionContainer Container =>
        new CastleWindsorDependencyInjectionContainer();

    protected virtual bool SupportsNamedServices => true;

    [Fact]
    public void Registered_service_is_resolved_as_single_call()
    {
        var c = Container;
        c.RegisterService<ITestService, TestService>(
            ServiceLifetime.SingleCall);

        var svc = c.GetService<ITestService>();
        Assert.NotNull(svc);
        Assert.IsType<TestService>(svc);

        var svc2 = c.GetService<ITestService>();
        Assert.NotNull(svc2);
        Assert.IsType<TestService>(svc2);
        Assert.NotSame(svc, svc2);
    }

    [Fact]
    public void Registered_service_is_resolved_as_singleton()
    {
        var c = Container;
        c.RegisterService<ITestService, TestService>(
            ServiceLifetime.Singleton);

        var svc = c.GetService<ITestService>();
        Assert.NotNull(svc);
        Assert.IsType<TestService>(svc);

        var svc2 = c.GetService<ITestService>();
        Assert.NotNull(svc2);
        Assert.IsType<TestService>(svc2);
        Assert.Same(svc, svc2);
    }

    [Fact]
    public void Registered_service_is_resolved_as_scoped()
    {
        var c = Container;
        c.RegisterService<ITestService, TestService>(ServiceLifetime.Scoped);
        c.RegisterService<IAsyncService, AsyncService>(ServiceLifetime.Singleton);
        c.RegisterService<IServiceWithDeps, ServiceWithDeps>(ServiceLifetime.SingleCall);

        using (c.CreateScope())
        {
            var svc = c.GetService<ITestService>();
            Assert.NotNull(svc);
            Assert.IsType<TestService>(svc);

            var scv3 = c.GetService<IServiceWithDeps>();
            Assert.NotNull(scv3);
            Assert.IsType<ServiceWithDeps>(scv3);

            var svcDeps = scv3 as ServiceWithDeps;
            Assert.NotNull(svcDeps.AsyncService);
            Assert.NotNull(svcDeps.TestService1);
            Assert.NotNull(svcDeps.TestService2);
            Assert.Same(svcDeps.TestService1, svcDeps.TestService2);
        }
    }

    [Fact]
    public void Registered_factory_is_resolved_as_single_call()
    {
        var c = Container;
        c.RegisterService<ITestService>(
            () => new TestService(), ServiceLifetime.SingleCall);

        var svc = c.GetService<ITestService>();
        Assert.NotNull(svc);
        Assert.IsType<TestService>(svc);

        var svc2 = c.GetService<ITestService>();
        Assert.NotNull(svc2);
        Assert.IsType<TestService>(svc2);
        Assert.NotSame(svc, svc2);
    }

    [Fact]
    public void Registered_factory_is_resolved_as_singleton()
    {
        var c = Container;
        c.RegisterService<ITestService>(
            () => new TestService(), ServiceLifetime.Singleton);

        var svc = c.GetService<ITestService>();
        Assert.NotNull(svc);
        Assert.IsType<TestService>(svc);

        var svc2 = c.GetService<ITestService>();
        Assert.NotNull(svc2);
        Assert.IsType<TestService>(svc2);
        Assert.Same(svc, svc2);
    }

    [Fact]
    [SuppressMessage("Usage", "xUnit1030:Do not call ConfigureAwait in test method", Justification = "<Pending>")]
    public async Task Register_multiple_services_thread_safety()
    {
        if (!SupportsNamedServices)
        {
            Console.WriteLine("Named services not supported. Skipping...");
            return;
        }

        var c = Container;
        var t = new ConcurrentDictionary<int, int>();

        void RegisterCurrentThread() =>
            t.TryAdd(Thread.CurrentThread.ManagedThreadId, 0);

        void RegisterService()
        {
            RegisterCurrentThread();
            c.RegisterService<ITestService, TestService>(
                ServiceLifetime.SingleCall, Guid.NewGuid().ToString());
        }

        void RegisterFactory()
        {
            RegisterCurrentThread();
            c.RegisterService<ITestService>(() => new TestService(),
                ServiceLifetime.SingleCall, Guid.NewGuid().ToString());
        }

        var tasks = new List<Task>();
        for (var i = 0; i < 500; i++)
        {
            tasks.Add(Task.Run(RegisterService));
            tasks.Add(Task.Run(RegisterFactory));
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);

        // check if all registrations are there
        var regs = c.GetServiceRegistrations();
        Assert.NotNull(regs);
        Assert.Equal(1000, regs.Count());

        // check if we actually used many threads
        Console.WriteLine($"Registration threads: {t.Keys.Count}");
        Assert.True(t.Count > 1);
    }
}
