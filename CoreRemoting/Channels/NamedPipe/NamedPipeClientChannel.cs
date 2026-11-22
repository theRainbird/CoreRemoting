using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using CoreRemoting.Threading;

namespace CoreRemoting.Channels.NamedPipe;

/// <summary>
/// Client side Named Pipe channel implementation.
/// </summary>
public class NamedPipeClientChannel : IClientChannel, IRawMessageTransport
{
	private NamedPipeClientStream _pipeClient;
	private IRemotingClient _remotingClient;
	private CancellationTokenSource _cancellationTokenSource;
	private Task _receivingTask;
	private int _receivingTaskInstance;
	private bool _isConnected;
	private readonly AsyncLock _disposeLock;
	private readonly AsyncLock _sendLock;
	private bool _isDisposed;

	/// <summary>
	/// Creates a new instance of the NamedPipeClientChannel class.
	/// </summary>
	public NamedPipeClientChannel()
	{
		_cancellationTokenSource = new CancellationTokenSource();
		_disposeLock = new AsyncLock();
		_sendLock = new AsyncLock();
	}

	/// <summary>
	/// Event: Fires when a message is received from server.
	/// </summary>
	public event Action<byte[]> ReceiveMessage;

	/// <summary>
	/// Event: Fires when an error occurred.
	/// </summary>
	public event Action<string, Exception> ErrorOccured;

	/// <inheritdoc />
	public event Action Disconnected;

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

		if (_isConnected)
			return;

		try
		{
			// increment instance before detaching so the old loop knows it's obsolete
			var instance = Interlocked.Increment(ref _receivingTaskInstance);

			using var oldCts = Interlocked.Exchange(ref _cancellationTokenSource, new CancellationTokenSource());
			oldCts?.Cancel();

			await DetachReceivingTaskAsync()
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

			_isConnected = true;

			// Start receiving messages after connection is established
			_receivingTask = ReceiveMessagesAsync(instance, _cancellationTokenSource.Token);

			// Send handshake message immediately after connection
			await SendHandshakeAsync().ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			LastException = new NetworkException($"Failed to connect to named pipe server: {ex.Message}", ex);
			ErrorOccured?.Invoke($"Connection error: {ex.Message}", ex);
			throw;
		}
	}

	private async Task SendHandshakeAsync()
	{
		try
		{
			// Send client handshake message to trigger server handshake response
			// Only send if still connected
			if (_isConnected && _pipeClient?.IsConnected == true)
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
	/// Receives messages from the pipe until disconnected or cancelled.
	/// Reconnection is owned by <c>RemotingClient</c>; this loop never reconnects on its own.
	/// </summary>
	private async Task ReceiveMessagesAsync(int instance, CancellationToken ct)
	{
		var pipe = _pipeClient;

		try
		{
			while (!_isDisposed && _isConnected && !ct.IsCancellationRequested)
			{
				var messageData = await ReadMessageAsync(pipe, ct)
					.ConfigureAwait(false);

				if (messageData == null)
				{
					// EOF or read error: the connection is gone.
					// Exit the loop; the client's reconnect logic takes over.
					break;
				}

				// Empty messages are the handshake completion marker and are
				// handled internally by CoreRemoting, not delivered here.
				if (messageData.Length > 0)
					ReceiveMessage?.Invoke(messageData);
			}
		}
		catch (OperationCanceledException)
		{
			// Expected when disconnecting
		}
		catch (Exception ex)
		{
			if (_isConnected)
			{
				LastException = new NetworkException($"Error receiving message: {ex.Message}", ex);
				ErrorOccured?.Invoke($"Receive error: {ex.Message}", ex);
			}
		}
		finally
		{
			try
			{
				pipe?.Dispose();
			}
			catch
			{
				/* ignore disposal errors */
			}

			// the loop is the single fire point; a superseded loop must not
			// touch the state of the current connection
			if (Volatile.Read(ref _receivingTaskInstance) == instance)
			{
				_isConnected = false;
				Disconnected?.Invoke();
			}
		}
	}

	/// <summary>
	/// Atomically detaches the current receive loop and awaits its completion.
	/// </summary>
	private async Task DetachReceivingTaskAsync()
	{
		var loop = _receivingTask;
		_receivingTask = null;

		if (loop != null)
		{
			try
			{
				await loop.ConfigureAwait(false);
			}
			catch
			{
				/* handled in loop */
			}
		}
	}

	private static async Task<byte[]> ReadMessageAsync(NamedPipeClientStream pipe, CancellationToken ct)
	{
		if (pipe == null || !pipe.IsConnected)
			return null;

		try
		{
			// Read message length (4 bytes) - ensure we read exactly 4 bytes
			var lengthBuffer = new byte[4];
			var headerBytesRead = 0;
			while (headerBytesRead < 4)
			{
				var readNow = await pipe
					.ReadAsync(lengthBuffer, headerBytesRead, 4 - headerBytesRead, ct)
					.ConfigureAwait(false);
				if (readNow == 0)
					return null;
				headerBytesRead += readNow;
			}

			var messageLength = BitConverter.ToInt32(lengthBuffer, 0);

			// Allow large messages; keep a sanity cap at 1GB to avoid accidental OOM
			if (messageLength < 0 || messageLength > 1_073_741_824)
				throw new InvalidOperationException($"Invalid message length: {messageLength}");

			// Read message content
			var messageBuffer = new byte[messageLength];
			var totalBytesRead = 0;

			while (totalBytesRead < messageLength)
			{
				// Check connection before each read
				if (!pipe.IsConnected)
					return null;

				var bytesToRead = messageLength - totalBytesRead;
				var read = await pipe
					.ReadAsync(messageBuffer, totalBytesRead, bytesToRead, ct)
					.ConfigureAwait(false);

				if (read == 0)
					return null;

				totalBytesRead += read;
			}

			return messageBuffer;
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
		catch (Exception ex)
		{
			throw new NetworkException($"Failed to read message: {ex.Message}", ex);
		}
	}

	/// <summary>
	/// Closes the connection.
	/// </summary>
	/// <remarks>
	/// The receive loop is the single fire point of the <see cref="Disconnected"/>
	/// event: cancelling the token here makes the loop exit and fire the event
	/// (guarded by its instance check), so this method never fires it directly.
	/// </remarks>
	public async Task DisconnectAsync()
	{
		using (await _disposeLock)
		{
			// Mark as disconnected in any case
			_isConnected = false;

			using var cts = Interlocked.Exchange(ref _cancellationTokenSource, null);
			cts?.Cancel();

			// await the loop instead of a blocking Wait with timeout:
			// the cancellation unblocks the pending pipe read promptly
			await DetachReceivingTaskAsync()
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
	public bool IsConnected => !_isDisposed && _isConnected && _pipeClient?.IsConnected == true;

	/// <summary>
	/// Gets the raw message transport component for this connection.
	/// </summary>
	public IRawMessageTransport RawMessageTransport => this;

	/// <summary>
	/// Sends a message to the server.
	/// </summary>
	/// <param name="rawMessage">Raw message data</param>
	/// <remarks>
	/// Fails fast when the pipe is not connected. Reconnection is owned by
	/// <c>RemotingClient</c>; the send path must not trigger channel-level
	/// reconnects, because that would bypass the client's session semantics.
	/// </remarks>
	public async Task<bool> SendMessageAsync(byte[] rawMessage)
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

		using (await _sendLock)
		{
			try
			{
				// Check if stream is still connected
				if (!pipe.IsConnected)
				{
					LastException = new NetworkException("Named pipe is not connected.");
					return false;
				}

				// Send message length (4 bytes)
				var lengthBuffer = BitConverter.GetBytes(rawMessage.Length);
				await pipe.WriteAsync(lengthBuffer, 0, 4, CancellationToken.None).ConfigureAwait(false);

				// Send message content in chunks to avoid pipe write limits
				const int MaxChunkSize = 64 * 1024; // 64 KiB
				var offset = 0;
				while (offset < rawMessage.Length)
				{
					var toWrite = Math.Min(MaxChunkSize, rawMessage.Length - offset);
					await pipe.WriteAsync(rawMessage, offset, toWrite, CancellationToken.None)
						.ConfigureAwait(false);
					offset += toWrite;
				}

				await pipe.FlushAsync(CancellationToken.None).ConfigureAwait(false);

				return true;
			}
			catch (IOException ioEx) when (ioEx.Message.Contains("Broken pipe") || ioEx.Message.Contains("Connection"))
			{
				// Normal pipe disconnection scenario: the receive loop detects
				// the EOF and fires Disconnected; the client reconnects.
				LastException = new NetworkException($"Failed to send message: {ioEx.Message}", ioEx);
				return false;
			}
			catch (Exception ex)
			{
				var errorMessage = $"Failed to send message: {ex.Message}";
				LastException = new NetworkException(errorMessage, ex);
				ErrorOccured?.Invoke($"Send error: {ex.Message}", ex);
				return false;
			}
		}
	}

	/// <summary>
	/// Gets or sets the last exception.
	/// </summary>
	public NetworkException LastException { get; set; }

	private string GetPipeName()
	{
		// Use configured pipe name or default
		return _remotingClient.Config?.ChannelConnectionName ?? "CoreRemoting";
	}

	/// <summary>
	/// Disconnect and free managed resources.
	/// </summary>
	public async ValueTask DisposeAsync()
	{
		_isDisposed = true;
		await DisconnectAsync().ConfigureAwait(false);

		using var cts = Interlocked.Exchange(ref _cancellationTokenSource, null);
		cts?.Cancel();

		_disposeLock.Dispose();
		_sendLock.Dispose();
	}
}
