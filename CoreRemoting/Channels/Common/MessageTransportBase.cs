using System;
using System.Threading.Tasks;

namespace CoreRemoting.Channels.Common;

/// <summary>
/// Base class for raw message transports. Provides the common event set,
/// last-exception tracking and a unified send template with error handling.
/// </summary>
public abstract class MessageTransportBase : IRawMessageTransport
{
    /// <inheritdoc />
    public NetworkException LastException { get; set; }

    /// <summary>
    /// Event: fires when the transport is connected.
    /// </summary>
    public event Action Connected;

    /// <summary>
    /// Fires the <see cref="Connected"/> event.
    /// </summary>
    protected void OnConnected() =>
        Connected?.Invoke();

    /// <summary>
    /// Public entry point to fire the <see cref="Connected"/> event.
    /// Intended for composed channel bases that own a transport instance
    /// and need to signal connection completion from outside the transport.
    /// </summary>
    public void RaiseConnected() =>
        OnConnected();

    /// <inheritdoc />
    public event Action Disconnected;

    /// <summary>
    /// Fires the <see cref="Disconnected"/> event.
    /// </summary>
    protected void OnDisconnected() =>
        Disconnected?.Invoke();

    /// <inheritdoc />
    public event Action<byte[]> ReceiveMessage;

    /// <summary>
    /// Fires the <see cref="ReceiveMessage"/> event.
    /// </summary>
    protected void OnReceiveMessage(byte[] message) =>
        ReceiveMessage?.Invoke(message);

    /// <inheritdoc />
    public event Action<string, Exception> ErrorOccured;

    /// <summary>
    /// Fires the <see cref="ErrorOccured"/> event.
    /// </summary>
    protected void OnErrorOccured(string message, Exception ex) =>
        ErrorOccured?.Invoke(message, ex);

    /// <summary>
    /// Wraps the exception into <see cref="LastException"/> (if it isn't a
    /// <see cref="NetworkException"/> already) and fires the
    /// <see cref="ErrorOccured"/> event.
    /// </summary>
    /// <param name="ex">The exception to report</param>
    protected void ReportError(Exception ex)
    {
        LastException = ex as NetworkException ??
            new NetworkException(ex.Message, ex);

        OnErrorOccured(ex.Message, LastException);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Template method: delegates to <see cref="SendCoreAsync"/> and applies the
    /// unified error handling. Exceptions treated as normal shutdown by
    /// <see cref="IsNormalShutdown"/> are swallowed silently.
    /// </remarks>
    public virtual async Task<bool> SendMessageAsync(byte[] rawMessage)
    {
        try
        {
            return await SendCoreAsync(rawMessage)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            if (IsNormalShutdown(ex))
                return false;

            ReportSendError(ex);
            return false;
        }
    }

    /// <summary>
    /// Transport specific send operation.
    /// </summary>
    /// <param name="rawMessage">Raw message data</param>
    /// <returns>True, if the message was sent successfully</returns>
    protected abstract Task<bool> SendCoreAsync(byte[] rawMessage);

    /// <summary>
    /// Reports a failed send operation. The default implementation wraps and
    /// fires the error like <see cref="ReportError"/>.
    /// </summary>
    /// <param name="ex">The exception thrown by the send operation</param>
    protected virtual void ReportSendError(Exception ex) =>
        ReportError(ex);

    /// <summary>
    /// Determines whether the given exception represents a normal shutdown
    /// rather than a failure. Normal shutdowns don't fire the
    /// <see cref="ErrorOccured"/> event.
    /// </summary>
    /// <param name="ex">The exception to check</param>
    protected virtual bool IsNormalShutdown(Exception ex) => false;
}
