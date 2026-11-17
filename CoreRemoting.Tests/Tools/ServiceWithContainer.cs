using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CoreRemoting.DependencyInjection;

namespace CoreRemoting.Tests.Tools;
public interface IServiceWithContainer
{
    IDependencyInjectionContainer Container { get; }
}

public class ServiceWithContainer : IServiceWithContainer
{
    public IDependencyInjectionContainer Container { get; }

    public ServiceWithContainer(IDependencyInjectionContainer container)
    {
        Container = container;
    }
}