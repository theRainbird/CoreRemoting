using CoreRemoting;
using static Logger<Server>;

internal class SampleService : ISampleService
{
    public void SayHello()
    {
        var userName = RemotingSession.Current.Identity.Name;
        WriteLine($"Hello! You are authenticated as: {userName}.");
    }
}
