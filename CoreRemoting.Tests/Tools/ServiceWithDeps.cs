namespace CoreRemoting.Tests.Tools;

public class ServiceWithDeps : IServiceWithDeps
{
    public ServiceWithDeps(IAsyncService async, ITestService test1, ITestService test2)
    {
        AsyncService = async;
        TestService1 = test1;
        TestService2 = test2;
    }

    public IAsyncService AsyncService { get; }

    public ITestService TestService1 { get; }

    public ITestService TestService2 { get; }

    public void Hello()
    {
    }
}
