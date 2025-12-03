using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using CoreRemoting.Threading;

namespace CoreRemoting.Channels.NamedPipe;

/// <summary>
/// Simple synchronous Named Pipe server.
/// </summary>
public class SimpleNamedPipeServer : IDisposable
{
	private readonly string _pipeName;
	private bool _isRunning;

	public SimpleNamedPipeServer(string pipeName)
	{
		_pipeName = pipeName;
	}

	public void Start()
	{
		if (_isRunning)
			return;

		_isRunning = true;
	}

	public void Stop()
	{
		if (!_isRunning)
			return;

		_isRunning = false;
	}

	public void Dispose()
	{
		Stop();
	}

	public async Task<NamedPipeServerStream> AcceptClientAsync()
	{
		if (!_isRunning)
			throw new InvalidOperationException("Server is not running.");

		// Create a new server stream for each connection
		var serverStream = new NamedPipeServerStream(
			_pipeName,
			PipeDirection.InOut,
			NamedPipeServerStream.MaxAllowedServerInstances,
			PipeTransmissionMode.Byte,
			PipeOptions.Asynchronous);

		await serverStream.WaitForConnectionAsync().ConfigureAwait(false);
		return serverStream;
	}
}

/// <summary>
/// Simple synchronous Named Pipe connection.
/// </summary>
public class SimpleNamedPipeConnection : IRawMessageTransport, IDisposable
{
	private readonly string _connectionId;
	private readonly NamedPipeServerStream _serverStream;
	private readonly IRemotingServer _server;
	private RemotingSession _session;
	private readonly CancellationTokenSource _cancellationTokenSource;
	private bool _isDisposed;
	private readonly AsyncLock _sendLock;

	/// <summary>
	/// Event: Fires when a message is received from client.
	/// </summary>
	public event Action<byte[]> ReceiveMessage;

	/// <summary>
	/// Event: Fires when an error occurred.
	/// </summary>
	public event Action<string, Exception> ErrorOccured;

	/// <summary>
	/// Event: Fires when the connection is disconnected.
	/// </summary>
	public event Action Disconnected;

	/// <summary>
	/// Event: Fires when the connection is disposed.
	/// </summary>
	public event EventHandler Disposed;

	/// <summary>
	/// Gets or sets the last exception.
	/// </summary>
	public NetworkException LastException { get; set; }

	public SimpleNamedPipeConnection(string connectionId, NamedPipeServerStream serverStream, IRemotingServer server)
	{
		_connectionId = connectionId ?? throw new ArgumentNullException(nameof(connectionId));
		_serverStream = serverStream ?? throw new ArgumentNullException(nameof(serverStream));
		_server = server ?? throw new ArgumentNullException(nameof(server));
		_cancellationTokenSource = new CancellationTokenSource();
		_sendLock = new AsyncLock();
	}

	public async Task HandleClientAsync()
	{
		try
		{
			// Start receiving messages
			await StartReceivingAsync().ConfigureAwait(false);
		}
		finally
		{
			// Fire disconnected event before disposing
			Disconnected?.Invoke();
			await DisposeAsync().ConfigureAwait(false);
		}
	}

	private async Task StartReceivingAsync()
	{
		try
		{
			// Create session immediately for NamedPipe connections
			// This ensures proper session context for scoped services
			if (!CreateSessionAsNeeded(null))
			{
				// Session creation failed, stop processing
				return;
			}

			// Continue reading messages
			while (!_cancellationTokenSource.Token.IsCancellationRequested && _serverStream.IsConnected)
			{
				var messageData = await ReadMessageAsync().ConfigureAwait(false);
				if (messageData == null)
					break;

				// Skip empty handshake messages
				if (messageData.Length > 0)
				{
					// Debug logging for NamedPipe message handling
					Console.Error.WriteLine(
						$"[NamedPipe] Received message of {messageData.Length} bytes for session {_connectionId}");
					ReceiveMessage?.Invoke(messageData);
				}
			}
		}
		catch (Exception ex)
		{
			if (!_isDisposed)
			{
				LastException = new NetworkException($"Connection error: {ex.Message}", ex);
				ErrorOccured?.Invoke($"Connection {_connectionId} error: {ex.Message}", ex);
			}
		}
	}

	private async Task<byte[]> ReadMessageAsync()
	{
		try
		{
			if (_serverStream == null || !_serverStream.IsConnected)
				return null;

			// Read message length (4 bytes) - ensure exactly 4 bytes are read
			var lengthBuffer = new byte[4];
			var headerBytesRead = 0;
			while (headerBytesRead < 4)
			{
				var readNow = await _serverStream
					.ReadAsync(lengthBuffer, headerBytesRead, 4 - headerBytesRead, _cancellationTokenSource.Token)
					.ConfigureAwait(false);
				if (readNow == 0)
					return null;
				headerBytesRead += readNow;
			}

			var messageLength = BitConverter.ToInt32(lengthBuffer, 0);

			// Allow large messages with a sanity cap at 1GB to avoid OOM
			if (messageLength < 0 || messageLength > 1_073_741_824)
				throw new InvalidOperationException($"Invalid message length: {messageLength}");

			// Read message content
			var messageBuffer = new byte[messageLength];
			var totalBytesRead = 0;

			while (totalBytesRead < messageLength)
			{
				if (!_serverStream.IsConnected)
				{
					// Connection lost during read
					return null;
				}

				var bytesToRead = messageLength - totalBytesRead;
				var read = await _serverStream
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
			return null;
		}
		catch (Exception ex)
		{
			throw new NetworkException($"Failed to read message: {ex.Message}", ex);
		}
	}

	private bool CreateSessionAsNeeded(Dictionary<string, object> metadata)
	{
		if (_session != null)
			return false;

		_session = _server.SessionRepository.CreateSession(
			null,
			$"NamedPipe:{_connectionId}",
			_server,
			this);

		_session.BeforeDispose += BeforeDisposeSession;

		return true;
	}

	private void BeforeDisposeSession()
	{
		_session = null;
		// Don't dispose the connection here - just like in WebSocket implementation
		// The connection should only be disposed when the client actually disconnects
		// or when the server stops. This prevents premature disconnection.
	}

	public async Task<bool> SendMessageAsync(byte[] rawMessage)
	{
		if (_isDisposed || rawMessage == null || _serverStream == null)
			return false;

		using (await _sendLock)
		{
			try
			{
				if (!_serverStream.IsConnected)
					return false;

				var lengthBuffer = BitConverter.GetBytes(rawMessage.Length);
				await _serverStream.WriteAsync(lengthBuffer, 0, 4, _cancellationTokenSource.Token)
					.ConfigureAwait(false);

				// Send content in chunks to bypass OS pipe write size limits
				const int MaxChunkSize = 64 * 1024; // 64 KiB
				var offset = 0;
				while (offset < rawMessage.Length)
				{
					var toWrite = Math.Min(MaxChunkSize, rawMessage.Length - offset);
					await _serverStream.WriteAsync(rawMessage, offset, toWrite, _cancellationTokenSource.Token)
						.ConfigureAwait(false);
					offset += toWrite;
				}

				await _serverStream.FlushAsync(_cancellationTokenSource.Token).ConfigureAwait(false);

				return true;
			}
			catch (IOException ioEx) when (ioEx.Message.Contains("Broken pipe") || ioEx.Message.Contains("Connection"))
			{
				return false;
			}
			catch (Exception ex)
			{
				LastException = new NetworkException($"Failed to send message: {ex.Message}", ex);
				ErrorOccured?.Invoke($"Connection {_connectionId} send error: {ex.Message}", ex);
				return false;
			}
		}
	}

	public async ValueTask DisposeAsync()
	{
		if (_isDisposed)
			return;

		_isDisposed = true;
		_cancellationTokenSource.Cancel();

		try
		{
			if (_serverStream != null)
			{
				await Task.Run(() => _serverStream.Disconnect()).ConfigureAwait(false);
				_serverStream.Dispose();
			}
		}
		catch
		{
			// Ignore disposal errors
		}

		_cancellationTokenSource.Dispose();
		_sendLock.Dispose();

		// Notify subscribers that this connection is disposed
		// Include information about whether this was due to session end
		Disposed?.Invoke(this, EventArgs.Empty);

		await Task.CompletedTask.ConfigureAwait(false);
	}

	public void Dispose()
	{
		DisposeAsync().AsTask().Wait();
	}
}