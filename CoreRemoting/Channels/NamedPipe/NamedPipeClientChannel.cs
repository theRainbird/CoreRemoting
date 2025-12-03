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
	private bool _isConnected;
	private readonly AsyncLock _disposeLock;
	private readonly AsyncLock _sendLock;
	private bool _isDisposed;
	private readonly AsyncLock _reconnectLock = new AsyncLock();

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
	/// Initializes the channel.
	/// </summary>
	/// <param name="client">CoreRemoting client</param>
	public void Init(IRemotingClient client)
	{
		_remotingClient = client ?? throw new ArgumentNullException(nameof(client));
	}

	/// <summary>
	/// Establish a connection with the server.
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
			// If previous connection was canceled, ensure we start with a fresh CTS
			if (_cancellationTokenSource.IsCancellationRequested)
			{
				_cancellationTokenSource.Dispose();
				_cancellationTokenSource = new CancellationTokenSource();
			}

			var pipeName = GetPipeName();

			// Dispose possible previous instance
			try
			{
				_pipeClient?.Dispose();
			}
			catch
			{
				/* ignore */
			}

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
			_receivingTask = Task.Run(ReceiveMessagesAsync, _cancellationTokenSource.Token);

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
			// Send initial empty message to trigger server handshake response
			// Only send if still connected
			if (_isConnected && _pipeClient?.IsConnected == true)
			{
				await SendMessageAsync(new byte[0]).ConfigureAwait(false);
			}
		}
		catch (Exception ex)
		{
			// Log handshake error but don't fail connection
			// Expected during authentication failures or connection drops
			Console.Error.WriteLine($"Handshake failed: {ex.Message}");
		}
	}

	private async Task ReceiveMessagesAsync()
	{
		try
		{
			// Process messages while connected; try to auto-reconnect if stream breaks
			while (!_cancellationTokenSource.Token.IsCancellationRequested && !_isDisposed)
			{
				// Ensure there is a connected stream
				if (!_isConnected || _pipeClient == null || !_pipeClient.IsConnected)
				{
					var reconnected = await TryReconnectAsync().ConfigureAwait(false);
					if (!reconnected)
					{
						// Wait a bit and retry, unless cancellation requested
						await Task.Delay(TimeSpan.FromMilliseconds(200), _cancellationTokenSource.Token)
							.ConfigureAwait(false);
						continue;
					}
				}

				var messageData = await ReadMessageAsync().ConfigureAwait(false);
				if (messageData == null)
				{
					// Lost connection while reading, retry
					continue;
				}

				// Check if this is the first message (handshake response)
				if (messageData.Length > 0)
				{
					// This is a real message, not handshake
					ReceiveMessage?.Invoke(messageData);
				}
				// If message is empty, it's the handshake completion
				// CoreRemoting will handle this internally
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
			if (_isConnected)
			{
				_isConnected = false;
				Disconnected?.Invoke();
			}
		}
	}

	private async Task<bool> TryReconnectAsync()
	{
		if (_isDisposed)
			return false;

		using (await _reconnectLock)
		{
			if (_isDisposed)
				return false;

			// If already connected, nothing to do
			if (_isConnected && _pipeClient?.IsConnected == true)
				return true;

			// Clean up old stream
			try
			{
				_pipeClient?.Dispose();
			}
			catch
			{
				/* ignore */
			}

			try
			{
				var pipeName = GetPipeName();
				_pipeClient = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

				var connectionTimeoutMs = _remotingClient.Config?.ConnectionTimeout > 0
					? _remotingClient.Config.ConnectionTimeout * 1000
					: -1;

				await _pipeClient.ConnectAsync(connectionTimeoutMs).ConfigureAwait(false);

				if (!_pipeClient.IsConnected)
				{
					_isConnected = false;
					return false;
				}

				_isConnected = true;
				await SendHandshakeAsync().ConfigureAwait(false);
				return true;
			}
			catch (Exception ex)
			{
				LastException = new NetworkException($"Failed to reconnect to named pipe server: {ex.Message}", ex);
				ErrorOccured?.Invoke($"Reconnect error: {ex.Message}", ex);
				_isConnected = false;
				return false;
			}
		}
	}

	private async Task<byte[]> ReadMessageAsync()
	{
		if (_pipeClient == null || !_pipeClient.IsConnected)
			return null;

		try
		{
			// Read message length (4 bytes) - ensure we read exactly 4 bytes
			var lengthBuffer = new byte[4];
			var headerBytesRead = 0;
			while (headerBytesRead < 4)
			{
				var readNow = await _pipeClient
					.ReadAsync(lengthBuffer, headerBytesRead, 4 - headerBytesRead, _cancellationTokenSource.Token)
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
				if (!_pipeClient.IsConnected)
					return null;

				var bytesToRead = messageLength - totalBytesRead;
				var read = await _pipeClient
					.ReadAsync(messageBuffer, totalBytesRead, bytesToRead, _cancellationTokenSource.Token)
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
	public async Task DisconnectAsync()
	{
		using (await _disposeLock)
		{
			// Mark as disconnected in any case
			var wasConnected = _isConnected;
			_isConnected = false;
			if (!_cancellationTokenSource.IsCancellationRequested)
				_cancellationTokenSource.Cancel();

			try
			{
				_receivingTask?.Wait(TimeSpan.FromSeconds(5));
			}
			catch (AggregateException)
			{
				// Expected when cancelling
			}

			try
			{
				if (_pipeClient != null)
				{
					if (_pipeClient.IsConnected)
					{
						_pipeClient.Close();
					}

					_pipeClient.Dispose();
				}
			}
			catch
			{
				// Ignore disposal errors
			}
			finally
			{
				_pipeClient = null;
				// Prepare for a future reconnect by resetting CTS
				try
				{
					_cancellationTokenSource.Dispose();
				}
				catch
				{
					/* ignore */
				}

				_cancellationTokenSource = new CancellationTokenSource();
				_receivingTask = null;
			}

			if (wasConnected)
				Disconnected?.Invoke();
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
	public async Task<bool> SendMessageAsync(byte[] rawMessage)
	{
		if (_isDisposed)
			return false;

		if (rawMessage == null)
			return false;

		// Try to auto-repair on demand
		if (!_isConnected || _pipeClient == null || !_pipeClient.IsConnected)
		{
			try
			{
				await ConnectAsync().ConfigureAwait(false);
			}
			catch
			{
				return false;
			}
		}

		using (await _sendLock)
		{
			try
			{
				// Check if stream is still connected
				if (!_pipeClient.IsConnected)
				{
					LastException = new NetworkException("Named pipe is not connected.");
					return false;
				}

				// Send message length (4 bytes)
				var lengthBuffer = BitConverter.GetBytes(rawMessage.Length);
				await _pipeClient.WriteAsync(lengthBuffer, 0, 4, _cancellationTokenSource.Token).ConfigureAwait(false);

				// Send message content in chunks to avoid pipe write limits
				const int MaxChunkSize = 64 * 1024; // 64 KiB
				var offset = 0;
				while (offset < rawMessage.Length)
				{
					var toWrite = Math.Min(MaxChunkSize, rawMessage.Length - offset);
					await _pipeClient.WriteAsync(rawMessage, offset, toWrite, _cancellationTokenSource.Token)
						.ConfigureAwait(false);
					offset += toWrite;
				}

				await _pipeClient.FlushAsync(_cancellationTokenSource.Token).ConfigureAwait(false);

				return true;
			}
			catch (IOException ioEx) when (ioEx.Message.Contains("Broken pipe") || ioEx.Message.Contains("Connection"))
			{
				// Normal pipe disconnection scenario: try a single immediate reconnect
				try
				{
					await DisconnectAsync().ConfigureAwait(false);
					await ConnectAsync().ConfigureAwait(false);
					// retry once
					return await SendMessageAsync(rawMessage).ConfigureAwait(false);
				}
				catch
				{
					return false;
				}
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
		_cancellationTokenSource.Dispose();
		_disposeLock.Dispose();
		_sendLock.Dispose();
	}
}