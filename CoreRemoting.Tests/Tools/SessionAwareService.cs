using System;
using System.Threading;

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

        public void CloseSession()
        {
            RemotingSession.Current.Close();
            Thread.Sleep(TimeSpan.FromSeconds(0.8));
        }
    }
}
