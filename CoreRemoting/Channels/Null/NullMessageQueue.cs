using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using CoreRemoting.Threading;

namespace CoreRemoting.Channels.Null;

/// <summary>
/// Simple in-process channel, request/response queue manager.
/// </summary>
public class NullMessageQueue
{
    /// <summary>
    /// Synchronization events: endpoint or sender:receiver -> reset event
    /// </summary>
    private static ConcurrentDictionary<string, AsyncManualResetEvent> Events { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Incoming message queues: endpoint or sender:receiver -> message queue
    /// </summary>
    private static ConcurrentDictionary<string, ConcurrentQueue<NullMessage>> Queues { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registered message listeners: endpoint -> endpoint
    /// </summary>
    private static ConcurrentDictionary<string, string> Listeners { get; } = new();

    /// <summary>
    /// Adds a new listener.
    /// </summary>
    /// <param name="endpoint">Listening endpoint.</param>
    public static void StartListener(string endpoint) =>
        Listeners.TryAdd(endpoint, endpoint);

    /// <summary>
    /// Stops the specified listener.
    /// </summary>
    /// <param name="endpoint">Listening endpoint.</param>
    public static void StopListener(string endpoint) =>
        Listeners.TryRemove(endpoint, out _);

    /// <summary>
    /// Clears all static state. Useful for test cleanup.
    /// </summary>
    public static void ClearAll()
    {
        Events.Clear();
        Queues.Clear();
        Listeners.Clear();
    }

    /// <summary>
    /// Connects to the specified listener endpoint.
    /// </summary>
    /// <param name="endpoint">Listener endpoint</param>
    /// <param name="metadata">Optional metadata parameters</param>
    public static string Connect(string endpoint, Dictionary<string, string> metadata = null)
    {
        if (Listeners.TryGetValue(endpoint, out var _))
        {
            var sender = Guid.NewGuid().ToString();
            SendMessage(endpoint, sender, endpoint, [], metadata);
            return sender;
        }

        throw new Exception($"No listener is registered for endpoint: {endpoint}");
    }

    /// <summary>
    /// Sends a message to the specified endpoint.
    /// </summary>
    /// <param name="sender">Sender endpoint.</param>
    /// <param name="receiver">Receiver endpoint.</param>
    /// <param name="message">Message to send.</param>
    /// <param name="metadata">Message metadata.</param>
    public static void SendMessage(string sender, string receiver, byte[] message, Dictionary<string, string> metadata = null) =>
        SendMessage($"{sender}:{receiver}", sender, receiver, message, metadata);

    /// <summary>
    /// Sends a message to the specified endpoint.
    /// </summary>
    /// <param name="address">Address, either sender:receiver or just receiver for incoming connections.</param>
    /// <param name="sender">Sender endpoint.</param>
    /// <param name="receiver">Receiver endpoint.</param>
    /// <param name="message">Message to send.</param>
    /// <param name="metadata">Message metadata.</param>
    private static void SendMessage(string address, string sender, string receiver, byte[] message, Dictionary<string, string> metadata)
    {
        var adr = address ?? $"{sender}:{receiver}";
        var mre = Events.GetOrAdd(adr, address => new(false));
        var queue = Queues.GetOrAdd(adr, address => new());
        queue.Enqueue(new(sender, receiver, message, metadata));
        mre.Set();
    }

    /// <summary>
    /// Receives all pending messages from the specified channel.
    /// </summary>
    /// <param name="address">Address, either sender:receiver or receiver for incoming connections.</param>
    /// <param name="sender">Sender endpoint.</param>
    /// <param name="receiver">Receiver endpoint.</param>
    /// <param name="cancellationToken">Token used to cancel the wait. When cancelled the enumerator ends
    /// without draining the queue so a cancelled receiver never steals a pending message.</param>
    public static async IAsyncEnumerable<NullMessage> ReceiveMessagesAsync(
        string address, string sender, string receiver,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var adr = address ?? $"{sender}:{receiver}";
        var mre = Events.GetOrAdd(adr, address => new(false));
        var queue = Queues.GetOrAdd(adr, address => new());

        if (cancellationToken.CanBeCanceled)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (cancellationToken.Register(() => tcs.TrySetResult(true)))
            {
                await Task.WhenAny(mre.WaitAsync(), tcs.Task).ConfigureAwait(false);
            }
        }
        else
        {
            await mre.WaitAsync().ConfigureAwait(false);
        }

        if (cancellationToken.IsCancellationRequested)
            yield break;

        mre.Reset();

        while (queue.TryDequeue(out var message))
            yield return message;
    }
}
