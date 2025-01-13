using System;
using System.Threading;
using System.Threading.Tasks;

namespace CoreRemoting.Tests.Tools
{
    internal class SessionAwareService : ISessionAwareService
    {
        public SessionAwareService()
        {
            CurrentSession = RemotingSession.Current;
            if (CurrentSession == null)
                throw new ArgumentNullException(nameof(CurrentSession));

            if (CurrentSession.ClientAddress == null)
                throw new ArgumentNullException(nameof(CurrentSession.ClientAddress));
            Console.WriteLine(CurrentSession.ClientAddress);
        }

        public RemotingSession CurrentSession { get; }

        public bool HasSameSessionInstance =>
            ReferenceEquals(CurrentSession, RemotingSession.Current);

        public string ClientAddress =>
            CurrentSession.ClientAddress;

        public async Task Wait(double seconds) =>
            await Task.Delay(TimeSpan.FromSeconds(seconds));

        public async Task CloseSession(double seconds)
        {
            RemotingSession.Current.Close();
            await Wait(seconds);
        }
    }
}
