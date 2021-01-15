using System;
using System.Diagnostics.CodeAnalysis;

namespace CoreRemoting.ClassicRemotingApi
{
    public static class DefaultRemotingInfrastructure
    {
        private static WeakReference<IRemotingClient> _defaultRemotingClientRef;
        private static WeakReference<IRemotingServer> _defaultRemotingServerRef;

        [SuppressMessage("ReSharper", "ArrangeAccessorOwnerBody")]
        public static IRemotingClient DefaultRemotingClient
        {
            get
            {
                if (_defaultRemotingClientRef == null)
                    return null;

                _defaultRemotingClientRef.TryGetTarget(out var defaultClient);

                return defaultClient;
            }
            internal set
            {
                _defaultRemotingClientRef = 
                    value == null 
                        ? null 
                        : new WeakReference<IRemotingClient>(value);
            }
        }
        
        [SuppressMessage("ReSharper", "ArrangeAccessorOwnerBody")]
        public static IRemotingServer DefaultRemotingServer
        {
            get
            {
                if (_defaultRemotingServerRef == null)
                    return null;

                _defaultRemotingServerRef.TryGetTarget(out var defaultServer);

                return defaultServer;
            }
            internal set
            {
                _defaultRemotingServerRef = 
                    value == null 
                        ? null 
                        : new WeakReference<IRemotingServer>(value);
            }
        }
       
        public static ClientConfig DefaultClientConfig { get; set; }
        
        public static ServerConfig DefaultServerConfig { get; set; }
    }
}