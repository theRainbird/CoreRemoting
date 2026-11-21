using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using CoreRemoting.Toolbox;

namespace CoreRemoting.RemoteDelegates;

/// <summary>
/// Stores client-side event subscriptions in a replayable form, so that they can be
/// re-established automatically after a reconnect (e.g. after a server restart).
/// </summary>
/// <remarks>
/// For every <c>add_EventName</c> accessor call the registry remembers the target
/// service name, the accessor method and the already mapped arguments (which contain
/// the <see cref="RemoteDelegateInfo"/> with the stable handler key). Repeating the
/// stored call on a new connection recreates the exact same subscription, including
/// the multicast counts of .NET events.
///
/// The registry mirrors the semantics of <see cref="SubscriptionTracker"/>: a
/// <c>remove_EventName</c> call decrements the stored add count, and the entry
/// disappears once the subscription is fully unsubscribed.
///
/// This is the client-side counterpart of the server's event hubs. It never talks
/// to the network itself; <see cref="RemotingClient"/> decides when to replay.
/// </remarks>
internal sealed class SubscriptionReplayRegistry
{
    private sealed record ReplayEntry(
        string ServiceName,
        MethodInfo AddMethod,
        object[] MappedArgs,
        int Count);

    private readonly ConcurrentDictionary<Guid, ReplayEntry> _entries = new();

    /// <summary>
    /// Gets the number of distinct handler keys with a positive subscription count.
    /// </summary>
    public int Count => _entries.Count;

    /// <summary>
    /// Records an event accessor call for potential replay.
    /// Non-event-accessor methods and empty handler keys are ignored.
    /// </summary>
    /// <param name="serviceName">Name of the remote service owning the event.</param>
    /// <param name="method">The intercepted <c>add_*</c> or <c>remove_*</c> accessor method.</param>
    /// <param name="mappedArgs">Mapped arguments of the accessor call (contains the handler key).</param>
    /// <param name="handlerKey">Handler key extracted from <paramref name="mappedArgs"/>.</param>
    public void Track(string serviceName, MethodInfo method, object[] mappedArgs, Guid handlerKey)
    {
        if (handlerKey == Guid.Empty)
            return;

        if (!method.IsEventAccessor(out _, out var isSubscription))
            return;

        if (isSubscription)
        {
            _entries.AddOrUpdate(
                handlerKey,
                _ => new ReplayEntry(serviceName, method, mappedArgs, 1),
                (_, existing) => existing with { Count = existing.Count + 1 });
            return;
        }

        // Mirror the tracker's multicast semantics: a remove decrements the stored
        // add count; the entry disappears once fully unsubscribed.
        while (true)
        {
            if (!_entries.TryGetValue(handlerKey, out var existing))
                return;

            var newCount = existing.Count - 1;

            if (newCount <= 0)
            {
                if (_entries.TryRemove(handlerKey, out _))
                    return;
            }
            else if (_entries.TryUpdate(handlerKey, existing with { Count = newCount }, existing))
                return;
        }
    }

    /// <summary>
    /// Removes a single handler key from the registry.
    /// </summary>
    /// <param name="handlerKey">Handler key to forget.</param>
    public void Remove(Guid handlerKey) =>
        _entries.TryRemove(handlerKey, out _);

    /// <summary>
    /// Removes multiple handler keys from the registry (e.g. on proxy shutdown).
    /// </summary>
    /// <param name="handlerKeys">Handler keys to forget.</param>
    public void RemoveRange(IEnumerable<Guid> handlerKeys)
    {
        if (handlerKeys == null)
            return;

        foreach (var key in handlerKeys)
            _entries.TryRemove(key, out _);
    }

    /// <summary>
    /// Takes a snapshot of all replayable entries.
    /// </summary>
    /// <returns>A list of (handler key, service name, add accessor, mapped args, count) tuples.</returns>
    public List<(Guid HandlerKey, string ServiceName, MethodInfo AddMethod, object[] MappedArgs, int Count)> Snapshot()
    {
        var snapshot = new List<(Guid, string, MethodInfo, object[], int)>(_entries.Count);

        foreach (var entry in _entries)
            snapshot.Add((entry.Key, entry.Value.ServiceName, entry.Value.AddMethod, entry.Value.MappedArgs, entry.Value.Count));

        return snapshot;
    }

    /// <summary>
    /// Removes all entries from the registry.
    /// </summary>
    public void Clear() =>
        _entries.Clear();
}
