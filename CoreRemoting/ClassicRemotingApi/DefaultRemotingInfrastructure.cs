using System;
using System.Diagnostics.CodeAnalysis;

namespace CoreRemoting.ClassicRemotingApi
{
    public class DefaultRemotingInfrastructure
    {
        private WeakReference<IRemotingClient> _defaultRemotingClientRef;
        private WeakReference<IRemotingServer> _defaultRemotingServerRef;

        [SuppressMessage("ReSharper", "ArrangeAccessorOwnerBody")]
        public IRemotingClient DefaultRemotingClient
        {
            get
            {
                if (_defaultRemotingClientRef == null)
                    return null;

                _defaultRemotingClientRef.TryGetTarget(out var defaultClient);

                return defaultClient;
            }
            set
            {
                _defaultRemotingClientRef = 
                    value == null 
                        ? null 
                        : new WeakReference<IRemotingClient>(value);
            }
        }
        
        [SuppressMessage("ReSharper", "ArrangeAccessorOwnerBody")]
        public IRemotingServer DefaultRemotingServer
        {
            get
            {
                if (_defaultRemotingServerRef == null)
                    return null;

                _defaultRemotingServerRef.TryGetTarget(out var defaultServer);

                return defaultServer;
            }
            set
            {
                _defaultRemotingServerRef = 
                    value == null 
                        ? null 
                        : new WeakReference<IRemotingServer>(value);
            }
        }
       
        public ClientConfig DefaultClientConfig { get; set; }
        
        public ServerConfig DefaultServerConfig { get; set; }
        
        #region Singleton implementation
        
        private static DefaultRemotingInfrastructure _singleton;
        
        [SuppressMessage("ReSharper", "InconsistentNaming")] 
        private static readonly object _singletonLock = new object();

        public static DefaultRemotingInfrastructure Singleton
        {
            get
            {
                if (_singleton == null)
                {
                    lock (_singletonLock)
                    {
                        if (_singleton == null)
                            _singleton = new DefaultRemotingInfrastructure();
                    }
                }

                return _singleton;
            }
        }

        /// <summary>
        /// Disallow public construction.
        /// </summary>
        private DefaultRemotingInfrastructure() { }
        
        #endregion
    }
}