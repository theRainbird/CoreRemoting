using System;
using CoreRemoting.RemoteDelegates;

namespace CoreRemoting.Tests.Tools;

public class ServiceWithHiddenDeps(IDelegateProxyFactory hiddenService) : IServiceWithHiddenDeps
{
    public bool IsImported
    {
        get
        {
            // check that hidden service is imported
            if (hiddenService == null)
                return false;

            // check that it actually works
            var test = hiddenService.Create(typeof(Action), _ => null);
            return test != null;
        }
    }
}
