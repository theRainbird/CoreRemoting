namespace CoreRemoting.Tests.Tools;

public interface IBaseService
{
    bool BaseMethod();

    string Version { get; }
}