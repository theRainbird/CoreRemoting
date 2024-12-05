using System;

namespace CoreRemoting.Tests.Tools;

public class FailingService : IFailingService
{
    public FailingService()
    {
        Console.WriteLine("FailingService constructor was called!");
        throw new NotImplementedException();
    }

    public void Hello()
    {
    }
}
