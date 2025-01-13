using System.Threading.Tasks;
using System;

namespace CoreRemoting.Tests.Tools
{
    public interface ISessionAwareService
    {
        bool HasSameSessionInstance { get; }

        string ClientAddress { get; }

        Task Wait(double seconds);

        Task CloseSession(double seconds);
    }
}
