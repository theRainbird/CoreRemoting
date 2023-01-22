namespace CoreRemoting.Tests.Tools;

public class GenericEchoService : IGenericEchoService
{
    public T Echo<T>(T value)
    {
        return value;
    }
}