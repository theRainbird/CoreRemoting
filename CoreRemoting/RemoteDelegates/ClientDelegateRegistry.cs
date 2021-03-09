using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace CoreRemoting.RemoteDelegates
{
    /// <summary>
    /// Registry for client delegates paired with service proxies.
    /// </summary>
    public sealed class ClientDelegateRegistry
    {
        private readonly ConcurrentDictionary<Guid, ClientDelegateInfo> _registry;

        /// <summary>
        /// Creates a new instance of the ClientDelegateRegistry class.
        /// </summary>
        public ClientDelegateRegistry()
        {
            _registry = new ConcurrentDictionary<Guid, ClientDelegateInfo>();
        }

        /// <summary>
        /// Registers a client delegate as callback target for remote delegate invocation.
        /// </summary>
        /// <param name="clientDelegate">Client delegate</param>
        /// <param name="serviceProxy">Service proxy of the remote service</param>
        /// <returns>Unique handler key</returns>
        /// <exception cref="ArgumentNullException">Thrown an argument is null</exception>
        /// <exception cref="ApplicationException">Thrown, if a race condition occurs while adding the client delegate to the registry</exception>
        public Guid RegisterClientDelegate(Delegate clientDelegate, object serviceProxy)
        {
            if (clientDelegate == null)
                throw new ArgumentNullException(nameof(clientDelegate));

            if (serviceProxy == null)
                throw new ArgumentNullException(nameof(serviceProxy));
            
            var handlerKey = FindDelegate(clientDelegate);

            if (handlerKey == Guid.Empty)
            {
                handlerKey = Guid.NewGuid();

                if (_registry.TryAdd(handlerKey, new ClientDelegateInfo(clientDelegate, serviceProxy)))
                    return handlerKey;
                
                throw new ApplicationException("Unable to add client delegate to registry. Possible race condition?");
            }

            return handlerKey;
        }

        /// <summary>
        /// Finds a specified registered client delegate and returns its unique handler key.
        /// </summary>
        /// <param name="delegate">Client delegate</param>
        /// <returns>Unique handler key</returns>
        [SuppressMessage("ReSharper", "PossibleMultipleEnumeration")]
        [SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
        public Guid FindDelegate(Delegate @delegate)
        {
            var foundHandlerKey =
                from pair in _registry
                let clientDelegateInfo = pair.Value
                where clientDelegateInfo.ClientDelegate == @delegate
                select pair.Key;

            return foundHandlerKey.Any() ? foundHandlerKey.First() : Guid.Empty;
        }

        /// <summary>
        /// Gets a registered client delegate by its handler key.
        /// </summary>
        /// <param name="handlerKey">Unique handler key</param>
        /// <returns>Client delegate</returns>
        public Delegate GetDelegateByHandlerKey(Guid handlerKey)
        {
            return _registry.ContainsKey(handlerKey) ? _registry[handlerKey].ClientDelegate : null;
        }

        /// <summary>
        /// Unregisters all client delegates that are paired withe a specified service proxy.
        /// </summary>
        /// <param name="serviceProxy">Service proxy</param>
        public void UnregisterClientDelegatesOfServiceProxy(object serviceProxy)
        {
            foreach (var handlerKey in from pair in _registry
                let clientDelegateInfo = pair.Value
                where clientDelegateInfo.ServiceProxy == serviceProxy
                select pair.Key)
            {
                _registry.TryRemove(handlerKey, out _);
            }
        }

        /// <summary>
        /// Unregisters a specified client delegate by its handler key.
        /// </summary>
        /// <param name="handlerKey">Unique handler key</param>
        [SuppressMessage("ReSharper", "UnusedVariable")]
        public void UnregisterClientDelegate(Guid handlerKey)
        {
            _registry.TryRemove(handlerKey, out var removedEntry);
        }
        
        /// <summary>
        /// Unregister a specified client delegate.
        /// </summary>
        /// <param name="clientDelegate">Client delegate</param>
        [SuppressMessage("ReSharper", "UnusedVariable")]
        public void UnregisterClientDelegate(Delegate clientDelegate)
        {
            var handlerKey = FindDelegate(clientDelegate);

            if (handlerKey == Guid.Empty)
                return;
            
            _registry.TryRemove(handlerKey, out var removedEntry);
        }

        /// <summary>
        /// Clear the entire client delegate registry.
        /// </summary>
        public void Clear()
        {
            _registry.Clear();
        }
    }
}