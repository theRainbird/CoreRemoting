using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using CoreRemoting.Channels.Common;
using CoreRemoting.Threading;

namespace CoreRemoting.Channels.NamedPipe;

/// <summary>
/// Client side Named Pipe channel implementation.
/// </summary>
public class NamedPipeClientChannel : ReadLoopTransportBase, IClientChannel
{
	private NamedPipeClientStream _pipeClient;
	private IRemotingClient _remotingClient;
	private readonly AsyncLock _disposeLock;
	private bool _isDisposed;

	// captured at loop start so the teardown disposes the pipe this loop
	// was created for, even if a reconnect already replaced it
	private NamedPipeClientStream _loopPipe;

	/// <summary>
	/// Creates a new instance of the NamedPipeClientChannel class.
	/// </summary>
	public NamedPipeClientChannel()
	{
		_disposeLock = new AsyncLock();
	}

	/// <summary>
	/// Tracing handler.
	/// </summary>
	public Action<string> TraceWriteLine { get; set; }

	/// <summary>
	/// Initializes the channel.
	/// </summary>
	/// <param name="client">CoreRemoting client</param>
	public void Init(IRemotingClient client)
	{
		_remotingClient = client ?? throw new ArgumentNullException(nameof(client));
	}

	/// <summary>
	/// Establish a connection with the server.
	/// A running receive loop from a previous connection is cancelled and awaited first.
	/// </summary>
	public async Task ConnectAsync()
	{
		if (_remotingClient == null)
			throw new InvalidOperationException("Channel is not initialized.");

		if (_isDisposed)
			throw new ObjectDisposedException(nameof(NamedPipeClientChannel));

		if (IsConnectedFlag)
			return;

		try
		{
			// invalidate the old loop before creating the new pipe, so a late
			// teardown of the previous connection can't fire a spurious disconnect
			await InvalidateReadLoopAsync()
				.ConfigureAwait(false);

			var pipeName = GetPipeName();

			_pipeClient = new NamedPipeClientStream(
				".",
				pipeName,
				PipeDirection.InOut,
				PipeOptions.Asynchronous);

			// Connect with timeout from client configuration
			var connectionTimeoutMs = _remotingClient.Config?.ConnectionTimeout > 0
				? _remotingClient.Config.ConnectionTimeout * 1000
				: -1; // Infinite timeout if 0 is specified
			await _pipeClient.ConnectAsync(connectionTimeoutMs).ConfigureAwait(false);

			if (!_pipeClient.IsConnected)
				throw new InvalidOperationException("Failed to establish named pipe connection.");

			IsConnectedFlag = true;

			// Start receiving messages after connection is established
			await StartListening()
				.ConfigureAwait(false);

			// Send handshake message immediately after connection
			await SendHandshakeAsync().ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			LastException = new NetworkException($"Failed to connect to named pipe server: {ex.Message}", ex);
			OnErrorOccured($"Connection error: {ex.Message}", ex);
			throw;
		}
	}

	private async Task SendHandshakeAsync()
	{
		try
		{
			// Send client handshake message to trigger server handshake response
			// Only send if still connected
			if (IsConnectedFlag && _pipeClient?.IsConnected == true)
			{
				var serializer = _remotingClient.Serializer;
				var handshake = _remotingClient.HandshakeMessage;
				var handshakeBytes = serializer.Serialize(handshake);

				await SendMessageAsync(handshakeBytes).ConfigureAwait(false);
			}
		}
		catch (Exception ex)
		{
			// Log handshake error but don't fail connection
			// Expected during authentication failures or connection drops
			TraceWriteLine?.Invoke($"Handshake failed: {ex.Message}");
		}
	}

	/// <summary>
	/// Captures the current pipe for this read loop.
	/// </summary>
	protected override void OnLoopStarting() =>
		_loopPipe = _pipeClient;

	/// <inheritdoc />
	protected override bool ShouldContinueReceiving(CancellationToken ct) =>
		!_isDisposed && IsConnectedFlag && !ct.IsCancellationRequested;

	/// <inheritdoc />
	/// <remarks>
	/// Empty messages are the handshake completion marker and are handled
	/// internally by CoreRemoting, not delivered here.
	/// </remarks>
	protected override bool ShouldDeliverMessage(byte[] message) =>
		message.Length > 0;

	/// <inheritdoc />
	protected override Task<byte[]> ReadNextAsync(CancellationToken ct) =>
		ReadMessageAsync(_loopPipe, ct);

	/// <inheritdoc />
	protected override void ReportReceiveError(Exception ex)
	{
		if (!IsConnectedFlag)
			return;

		LastException = new NetworkException($"Error receiving message: {ex.Message}", ex);
		OnErrorOccured($"Receive error: {ex.Message}", ex);
	}

	/// <inheritdoc />
	/// <remarks>
	/// The loop is the single fire point; a superseded loop must not touch the
	/// state of the current connection.
	/// </remarks>
	protected override async Task OnReadLoopEndedAsync(int instance)
	{
		try
		{
			_loopPipe?.Dispose();
		}
		catch
		{
			/* ignore disposal errors */
		}

		await base.OnReadLoopEndedAsync(instance)
			.ConfigureAwait(false);
	}

	private static async Task<byte[]> ReadMessageAsync(NamedPipeClientStream pipe, CancellationToken ct)
	{
		if (pipe == null || !pipe.IsConnected)
			return null;

		try
		{
			// Allow large messages; keep a sanity cap at 1GB to avoid accidental OOM
			return await LengthPrefixedFrame
				.ReadFrameAsync(pipe, 1_073_741_824, ct)
				.ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
			return null;
		}
		catch (ObjectDisposedException)
		{
			// pipe disposed during shutdown
			return null;
		}
		catch (IOException ioEx) when (ioEx.Message.Contains("Broken pipe") || ioEx.Message.Contains("Connection"))
		{
			// Normal pipe disconnection scenario
			return null;
		}
		catch (InvalidOperationException)
		{
			// invalid message length from the framing helper
			throw;
		}
		catch (Exception ex)
		{
			throw new NetworkException($"Failed to read message: {ex.Message}", ex);
		}
	}

	/// <summary>
	/// Closes the connection.
	/// </summary>
	/// <remarks>
	/// The receive loop is the single fire point of the <see cref="MessageTransportBase.Disconnected"/>
	/// event: cancelling the token here makes the loop exit and fire the event
	/// (guarded by its instance check), so this method never fires it directly.
	/// </remarks>
	public override async Task DisconnectAsync()
	{
		using (await _disposeLock)
		{
			// Mark as disconnected in any case
			IsConnectedFlag = false;

			CancelReadLoop();

			// await the loop instead of a blocking Wait with timeout:
			// the cancellation unblocks the pending pipe read promptly
			await DetachReadLoopAsync()
				.ConfigureAwait(false);

			try
			{
				_pipeClient?.Dispose();
			}
			catch
			{
				// Ignore disposal errors
			}
			finally
			{
				_pipeClient = null;
			}
		}
	}

	/// <summary>
	/// Gets whether the connection is established or not.
	/// </summary>
	public override bool IsConnected =>
		!_isDisposed && IsConnectedFlag && _pipeClient?.IsConnected == true;

	/// <summary>
	/// Gets the raw message transport component for this connection.
	/// </summary>
	public IRawMessageTransport RawMessageTransport => this;

	/// <inheritdoc />
	/// <remarks>
	/// Fails fast when the pipe is not connected. Reconnection is owned by
	/// <c>RemotingClient</c>; the send path must not trigger channel-level
	/// reconnects, because that would bypass the client's session semantics.
	/// </remarks>
	protected override async Task<bool> SendCoreAsync(byte[] rawMessage)
	{
		if (_isDisposed)
			return false;

		if (rawMessage == null)
			return false;

		var pipe = _pipeClient;
		if (pipe == null || !pipe.IsConnected)
		{
			LastException = new NetworkException("Named pipe is not connected.");
			return false;
		}

		using (await SendLock)
		{
			// Check if stream is still connected
			if (!pipe.IsConnected)
			{
				LastException = new NetworkException("Named pipe is not connected.");
				return false;
			}

			try
			{
				await LengthPrefixedFrame
					.WriteFrameAsync(pipe, rawMessage, CancellationToken.None)
					.ConfigureAwait(false);

				return true;
			}
			catch (IOException ioEx) when (ioEx.Message.Contains("Broken pipe") || ioEx.Message.Contains("Connection"))
			{
				// Normal pipe disconnection scenario: the receive loop detects
				// the EOF and fires Disconnected; the client reconnects.
				LastException = new NetworkException($"Failed to send message: {ioEx.Message}", ioEx);
				return false;
			}
		}
	}

	/// <inheritdoc />
	protected override void ReportSendError(Exception ex)
	{
		var errorMessage = $"Failed to send message: {ex.Message}";
		LastException = new NetworkException(errorMessage, ex);
		OnErrorOccured($"Send error: {ex.Message}", ex);
	}

	private string GetPipeName()
	{
		// Use configured pipe name or default
		return _remotingClient.Config?.ChannelConnectionName ?? "CoreRemoting";
	}

	/// <summary>
	/// Disconnect and free managed resources.
	/// </summary>
	public override async ValueTask DisposeAsync()
	{
		_isDisposed = true;
		await base.DisposeAsync().ConfigureAwait(false);

		_disposeLock.Dispose();
	}
}
