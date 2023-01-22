namespace CoreRemoting.Tests.Tools;

public interface IGenericEchoService
{
    T Echo<T>(T value);
}