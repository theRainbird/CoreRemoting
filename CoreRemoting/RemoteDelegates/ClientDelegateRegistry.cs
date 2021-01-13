using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace CoreRemoting.RemoteDelegates
{
    public sealed class ClientDelegateRegistry
    {
        private readonly ConcurrentDictionary<Guid, ClientDelegateInfo> _registry;

        public ClientDelegateRegistry()
        {
            _registry = new ConcurrentDictionary<Guid, ClientDelegateInfo>();
        }

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

        public Delegate GetDelegateByHandlerKey(Guid handlerKey)
        {
            return _registry.ContainsKey(handlerKey) ? _registry[handlerKey].ClientDelegate : null;
        }

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

        [SuppressMessage("ReSharper", "UnusedVariable")]
        public void UnregisterClientDelegate(Guid handlerKey)
        {
            _registry.TryRemove(handlerKey, out var removedEntry);
        }
        
        [SuppressMessage("ReSharper", "UnusedVariable")]
        public void UnregisterClientDelegate(Delegate clientDelegate)
        {
            var handlerKey = FindDelegate(clientDelegate);

            if (handlerKey == Guid.Empty)
                return;
            
            _registry.TryRemove(handlerKey, out var removedEntry);
        }

        public void Clear()
        {
            _registry.Clear();
        }
    }
}