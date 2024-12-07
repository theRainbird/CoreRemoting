namespace CoreRemoting.Tests.Tools
{
    public interface ISessionAwareService
    {
        bool HasSameSessionInstance { get; }

        string ClientAddress { get; }
    }
}
