using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace CoreRemoting.RemoteDelegates;

/// <summary>
/// Registry for client delegates paired with service proxies.
/// </summary>
public sealed class ClientDelegateRegistry
{
    private readonly ConcurrentDictionary<Guid, ClientDelegateInfo> _registry;
    private readonly ConcurrentDictionary<Delegate, Guid> _reverseIndex;

    /// <summary>
    /// Creates a new instance of the ClientDelegateRegistry class.
    /// </summary>
    public ClientDelegateRegistry()
    {
        _registry = new ConcurrentDictionary<Guid, ClientDelegateInfo>();
        _reverseIndex = new ConcurrentDictionary<Delegate, Guid>();
    }

    /// <summary>
    /// Registers a client delegate as callback target for remote delegate invocation.
    /// If the delegate is already registered, returns the existing handler key.
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

        // Fast path: already registered.
        if (_reverseIndex.TryGetValue(clientDelegate, out var existing))
            return existing;

        var info = new ClientDelegateInfo(clientDelegate, serviceProxy);

        // Whoever wins the reverse-index race owns the registration.
        var claimed = _reverseIndex.GetOrAdd(clientDelegate, info.HandlerKey);

        if (claimed != info.HandlerKey)
            return claimed; // lost the race, discard the freshly created info

        if (!_registry.TryAdd(info.HandlerKey, info))
        {
            // Should never happen for a fresh Guid, but leave no dangling entry behind.
            _reverseIndex.TryRemove(clientDelegate, out _);
            throw new ApplicationException("Unable to add client delegate to registry. Possible race condition?");
        }

        return info.HandlerKey;
    }

    /// <summary>
    /// Finds a specified registered client delegate and returns its unique handler key.
    /// </summary>
    /// <param name="delegate">Client delegate</param>
    /// <returns>Unique handler key, or <see cref="Guid.Empty"/> if not found</returns>
    [SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
    public Guid FindDelegate(Delegate @delegate)
    {
        if (@delegate == null)
            return Guid.Empty;

        return _reverseIndex.TryGetValue(@delegate, out var handlerKey)
            ? handlerKey
            : Guid.Empty;
    }

    /// <summary>
    /// Gets a registered client delegate by its handler key.
    /// </summary>
    /// <param name="handlerKey">Unique handler key</param>
    /// <returns>Client delegate, or <c>null</c> if not found</returns>
    public Delegate GetDelegateByHandlerKey(Guid handlerKey) =>
        _registry.TryGetValue(handlerKey, out var value) ? value.ClientDelegate : null;

    /// <summary>
    /// Gets registration info by its handler key.
    /// </summary>
    /// <param name="handlerKey">Unique handler key</param>
    /// <returns>Registration info, or <c>null</c> if not found</returns>
    public ClientDelegateInfo GetDelegateInfoByHandlerKey(Guid handlerKey) =>
        _registry.TryGetValue(handlerKey, out var value) ? value : null;

    /// <summary>
    /// Unregisters all client delegates that are paired with a specified service proxy.
    /// </summary>
    /// <param name="serviceProxy">Service proxy</param>
    public void UnregisterClientDelegatesOfServiceProxy(object serviceProxy)
    {
        if (serviceProxy == null)
            return;

        var keysToRemove = _registry
            .Where(pair => pair.Value.ServiceProxy == serviceProxy)
            .Select(pair => pair.Key)
            .ToArray();

        foreach (var key in keysToRemove)
        {
            if (_registry.TryRemove(key, out var info))
                _reverseIndex.TryRemove(info.ClientDelegate, out _);
        }
    }

    /// <summary>
    /// Unregisters a specified client delegate by its handler key.
    /// </summary>
    /// <param name="handlerKey">Unique handler key</param>
    internal void UnregisterClientDelegate(Guid handlerKey)
    {
        if (_registry.TryRemove(handlerKey, out var info))
            _reverseIndex.TryRemove(info.ClientDelegate, out _);
    }

    /// <summary>
    /// Unregisters a specified client delegate.
    /// </summary>
    /// <param name="clientDelegate">Client delegate</param>
    internal void UnregisterClientDelegate(Delegate clientDelegate)
    {
        if (clientDelegate == null)
            return;

        if (_reverseIndex.TryRemove(clientDelegate, out var handlerKey))
            _registry.TryRemove(handlerKey, out _);
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
