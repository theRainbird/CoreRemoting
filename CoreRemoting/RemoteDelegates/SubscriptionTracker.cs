using System;
using System.Collections.Generic;

namespace CoreRemoting.RemoteDelegates;

/// <summary>
/// Tracks subscriptions by their handler keys and provides a compact
/// checksum to compare subscription sets between client and server.
/// </summary>
/// <remarks>
/// The tracker counts operations rather than tracking state, so multicast
/// semantics of .NET events are preserved: adding the same subscription
/// twice increments the counter twice, removing it twice decrements it
/// twice. A subscription that is added twice will be invoked twice by the
/// server, and the checksum reflects that.
///
/// The checksum is a pair (count, sum) where sum is the 128-bit sum of
/// all handler keys accounted for. Both client and server use the same
/// algorithm, so a mismatch reliably indicates a lost operation.
///
/// Count and sum are updated together under a lock to guarantee that a
/// checksum is never observed with a mismatched count/sum pair.
///
/// Reading a Guid's halves via <see cref="BitConverter"/> honors the host
/// byte order. All platforms currently supported by .NET are little-endian,
/// so the checksum is deterministic across the client/server pair.
/// </remarks>
public sealed class SubscriptionTracker
{
    private readonly object _syncRoot = new();
    private int _count;
    private long _sumLo;
    private long _sumHi;

    /// <summary>
    /// Gets the subscription operation count (adds minus removes).
    /// </summary>
    public int Count
    {
        get { lock (_syncRoot) return _count; }
    }

    /// <summary>
    /// Gets the checksum of the current subscription set.
    /// </summary>
    public string Checksum
    {
        get
        {
            lock (_syncRoot)
                return Format(_count, _sumLo, _sumHi);
        }
    }

    /// <summary>
    /// Adds a subscription by its handler key.
    /// </summary>
    /// <param name="handlerKey">Handler key of the subscription to add.</param>
    public void Add(Guid handlerKey)
    {
        lock (_syncRoot)
            UpdateCore(handlerKey, +1);
    }

    /// <summary>
    /// Adds one or more subscriptions by their handler keys.
    /// </summary>
    /// <param name="handlerKeys">Handler keys of the subscriptions to add.</param>
    public void Add(IEnumerable<Guid> handlerKeys)
    {
        if (handlerKeys == null)
            return;

        if (handlerKeys is Guid[] array && array.Length == 0)
            return;

        lock (_syncRoot)
            foreach (var key in handlerKeys)
                UpdateCore(key, +1);
    }

    /// <summary>
    /// Removes a subscription by its handler key.
    /// </summary>
    /// <param name="handlerKey">Handler key of the subscription to remove.</param>
    public void Remove(Guid handlerKey)
    {
        lock (_syncRoot)
            UpdateCore(handlerKey, -1);
    }

    /// <summary>
    /// Removes one or more subscriptions by their handler keys.
    /// </summary>
    /// <param name="handlerKeys">Handler keys of the subscriptions to remove.</param>
    public void Remove(IEnumerable<Guid> handlerKeys)
    {
        if (handlerKeys == null)
            return;

        if (handlerKeys is Guid[] array && array.Length == 0)
            return;

        lock (_syncRoot)
            foreach (var key in handlerKeys)
                UpdateCore(key, -1);
    }

    /// <summary>
    /// Resets the tracker, optionally seeding it with the given handler keys.
    /// </summary>
    /// <param name="handlerKeys">Handler keys to seed, or <c>null</c> to clear.</param>
    public void Reset(IEnumerable<Guid> handlerKeys = null)
    {
        lock (_syncRoot)
        {
            _count = 0;
            _sumLo = 0;
            _sumHi = 0;

            if (handlerKeys != null)
                foreach (var key in handlerKeys)
                    UpdateCore(key, +1);
        }
    }

    /// <summary>
    /// Applies a single update operation. Must be called under <see cref="_syncRoot"/>.
    /// </summary>
    /// <param name="handlerKey">Handler key of the subscription.</param>
    /// <param name="sign">+1 to add, -1 to remove.</param>
    private void UpdateCore(Guid handlerKey, int sign)
    {
        var (lo, hi) = Parts(handlerKey);
        _count += sign;
        _sumLo = unchecked(_sumLo + sign * lo);
        _sumHi = unchecked(_sumHi + sign * hi);
    }

    private static string Format(int count, long lo, long hi) =>
        $"{count}:{unchecked((ulong)lo):x16}-{unchecked((ulong)hi):x16}";

    private static (long Lo, long Hi) Parts(Guid handlerKey)
    {
        var bytes = handlerKey.ToByteArray();
        return (BitConverter.ToInt64(bytes, 0), BitConverter.ToInt64(bytes, 8));
    }
}