using System;

namespace CoreRemoting.Tests.Tools
{
    internal class SessionAwareService : ISessionAwareService
    {
        public SessionAwareService()
        {
            CurrentSession = RemotingSession.Current;
            if (CurrentSession == null)
                throw new ArgumentNullException(nameof(CurrentSession));
        }

        public RemotingSession CurrentSession { get; }

        public bool HasSameSessionInstance =>
            ReferenceEquals(CurrentSession, RemotingSession.Current);
    }
}
