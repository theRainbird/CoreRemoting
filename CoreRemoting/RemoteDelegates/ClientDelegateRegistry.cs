using System;
using System.Collections.Concurrent;

namespace CoreRemoting.RemoteDelegates;

/// <summary>
/// Registry for client delegates paired with service proxies.
/// </summary>
/// <remarks>
/// A subscription is identified by the pair (delegate, service proxy), not
/// by the delegate alone: the same delegate subscribed to events of two
/// different proxies produces two independent registrations, so shutting
/// down one proxy doesn't affect the other.
/// </remarks>
public sealed class ClientDelegateRegistry
{
    private readonly ConcurrentDictionary<Guid, ClientDelegateInfo> _registry;
    private readonly ConcurrentDictionary<(Delegate, object), Guid> _reverseIndex;

    /// <summary>
    /// Creates a new instance of the ClientDelegateRegistry class.
    /// </summary>
    public ClientDelegateRegistry()
    {
        _registry = new ConcurrentDictionary<Guid, ClientDelegateInfo>();
        _reverseIndex = new ConcurrentDictionary<(Delegate, object), Guid>();
    }

    /// <summary>
    /// Registers a client delegate as callback target for remote delegate invocation.
    /// If the same delegate is already registered for the same service proxy,
    /// returns the existing handler key. Different proxies yield different keys
    /// even for the same delegate.
    /// </summary>
    /// <param name="clientDelegate">Client delegate</param>
    /// <param name="serviceProxy">Service proxy of the remote service</param>
    /// <returns>Unique handler key</returns>
    /// <exception cref="ArgumentNullException">Thrown if an argument is null</exception>
    /// <exception cref="ApplicationException">Thrown if a race condition occurs while adding the client delegate to the registry</exception>
    public Guid RegisterClientDelegate(Delegate clientDelegate, object serviceProxy)
    {
        if (clientDelegate == null)
            throw new ArgumentNullException(nameof(clientDelegate));

        if (serviceProxy == null)
            throw new ArgumentNullException(nameof(serviceProxy));

        var indexKey = (clientDelegate, serviceProxy);

        // Fast path: already registered for this proxy.
        if (_reverseIndex.TryGetValue(indexKey, out var existing))
            return existing;

        var info = new ClientDelegateInfo(clientDelegate, serviceProxy);

        // Whoever wins the reverse-index race owns the registration.
        var claimed = _reverseIndex.GetOrAdd(indexKey, info.HandlerKey);

        if (claimed != info.HandlerKey)
            return claimed; // lost the race, discard the freshly created info

        if (!_registry.TryAdd(info.HandlerKey, info))
        {
            // Should never happen for a fresh Guid, but leave no dangling entry behind.
            _reverseIndex.TryRemove(indexKey, out _);
            throw new ApplicationException("Unable to add client delegate to registry. Possible race condition?");
        }

        return info.HandlerKey;
    }

    /// <summary>
    /// Gets a registered client delegate by its handler key.
    /// </summary>
    /// <param name="handlerKey">Unique handler key</param>
    /// <returns>Client delegate, or <c>null</c> if not found</returns>
    public Delegate GetDelegateByHandlerKey(Guid handlerKey) =>
        _registry.TryGetValue(handlerKey, out var value) ? value.ClientDelegate : null;

    /// <summary>
    /// Unregisters all client delegates that are paired with a specified service proxy.
    /// Subscriptions made through other proxies are not affected, even if they use
    /// the same delegate instance.
    /// </summary>
    /// <param name="serviceProxy">Service proxy</param>
    public void UnregisterClientDelegatesOfServiceProxy(object serviceProxy)
    {
        if (serviceProxy == null)
            return;

        // Linear scan over all registrations, runs once per proxy shutdown.
        // The enumerator returned from the dictionary is safe to use
        // concurrently with reads and writes to the dictionary.
        foreach (var pair in _registry)
        {
            if (pair.Value.ServiceProxy != serviceProxy)
                continue;

            if (_registry.TryRemove(pair.Key, out var info))
                _reverseIndex.TryRemove((info.ClientDelegate, info.ServiceProxy), out _);
        }
    }

    /// <summary>
    /// Clears the entire client delegate registry.
    /// </summary>
    public void Clear()
    {
        _registry.Clear();
        _reverseIndex.Clear();
    }
}
