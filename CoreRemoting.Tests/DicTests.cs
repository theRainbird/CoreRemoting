using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreRemoting.DependencyInjection;
using CoreRemoting.Tests.Tools;
using Xunit;

namespace CoreRemoting.Tests
{
    public class DicTests
    {
        public virtual IDependencyInjectionContainer Container =>
            new CastleWindsorDependencyInjectionContainer();

        [Fact]
        public void Registered_service_is_resolved()
        {
            var c = Container;
            c.RegisterService<ITestService, TestService>(
                ServiceLifetime.SingleCall, "1");

            var svc = c.GetService<ITestService>("1");
            Assert.NotNull(svc);
            Assert.IsType<TestService>(svc);
        }

        [Fact]
        public void Registered_factory_is_resolved()
        {
            var c = Container;
            c.RegisterService<ITestService>(
                () => new TestService(), ServiceLifetime.SingleCall, "2");

            var svc = c.GetService<ITestService>("2");
            Assert.NotNull(svc);
            Assert.IsType<TestService>(svc);
        }

        [Fact]
        [SuppressMessage("Usage", "xUnit1030:Do not call ConfigureAwait in test method", Justification = "<Pending>")]
        public async Task Register_multiple_services_thread_safety()
        {
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
}
