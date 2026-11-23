using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using CoreRemoting.Channels.Common;
using CoreRemoting.RpcMessaging;

namespace CoreRemoting.Channels.NamedPipe;

/// <summary>
/// Simple synchronous Named Pipe server.
/// </summary>
public class SimpleNamedPipeServer : IDisposable
{
	private readonly string _pipeName;
	private bool _isRunning;

	/// <summary>
	/// Initializes a new instance of the <see cref="SimpleNamedPipeServer"/> class with the specified pipe name.
	/// </summary>
	/// <param name="pipeName">The name of the named pipe.</param>
	public SimpleNamedPipeServer(string pipeName)
	{
		_pipeName = pipeName;
	}

	/// <summary>
	/// Starts the named pipe server.
	/// </summary>
	public void Start()
	{
		if (_isRunning)
			return;

		_isRunning = true;
	}

	/// <summary>
	/// Stops the named pipe server.
	/// </summary>
	public void Stop()
	{
		if (!_isRunning)
			return;

		_isRunning = false;
	}

	/// <summary>
	/// Disposes the server and stops it.
	/// </summary>
	public void Dispose()
	{
		Stop();
	}

	/// <summary>
	/// Accepts a client connection asynchronously and returns the server stream.
	/// </summary>
	/// <returns>A task that represents the asynchronous operation. The task result contains the <see cref="NamedPipeServerStream"/> for the connected client.</returns>
	/// <exception cref="InvalidOperationException">Thrown if the server is not running.</exception>
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
public class SimpleNamedPipeConnection : ServerConnectionBase, IDisposable
{
	private readonly string _connectionId;
	private readonly NamedPipeServerStream _serverStream;
	private readonly CancellationTokenSource _cancellationTokenSource;
	private bool _isDisposed;

	/// <summary>
	/// Event: Fires when the connection is disposed.
	/// </summary>
	public event EventHandler Disposed;

	/// <summary>
	/// Tracing handler.
	/// </summary>
	public Action<string> TraceWriteLine { get; set; }

	/// <summary>
	/// Initializes a new instance of the <see cref="SimpleNamedPipeConnection"/> class.
	/// </summary>
	/// <param name="connectionId">The unique identifier for the connection.</param>
	/// <param name="serverStream">The named pipe server stream.</param>
	/// <param name="server">The remoting server instance.</param>
	public SimpleNamedPipeConnection(string connectionId, NamedPipeServerStream serverStream, IRemotingServer server)
	{
		_connectionId = connectionId ?? throw new ArgumentNullException(nameof(connectionId));
		_serverStream = serverStream ?? throw new ArgumentNullException(nameof(serverStream));
		RemotingServer = server ?? throw new ArgumentNullException(nameof(server));
		_cancellationTokenSource = new CancellationTokenSource();
	}

	/// <summary>
	/// Handles the client connection asynchronously.
	/// </summary>
	/// <returns>A task that represents the asynchronous operation.</returns>
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
			OnDisconnected();
			await DisposeAsync().ConfigureAwait(false);
		}
	}

	private async Task StartReceivingAsync()
	{
		try
		{
			// The first message is the handshake
			var handshakeBytes = await ReadMessageAsync().ConfigureAwait(false);
			var handshake = handshakeBytes is { Length: > 0 }
				? RemotingServer.Serializer.Deserialize<ClientHandshakeMessage>(handshakeBytes)
				: new ClientHandshakeMessage();

			handshake.ClientAddress = $"NamedPipe:{_connectionId}";

			// Create session immediately for NamedPipe connections
			// This ensures proper session context for scoped services
			var sessionCreated = await CreateSessionAsNeeded(handshake).ConfigureAwait(false);
			if (!sessionCreated)
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
					TraceWriteLine?.Invoke(
						$"[NamedPipe] Received message of {messageData.Length} bytes for session {_connectionId}");
					OnReceiveMessage(messageData);
				}
			}
		}
		catch (Exception ex)
		{
			if (!_isDisposed)
			{
				LastException = new NetworkException($"Connection error: {ex.Message}", ex);
				OnErrorOccured($"Connection {_connectionId} error: {ex.Message}", ex);
			}
		}
	}

	/// <inheritdoc />
	/// <remarks>
	/// This connection pumps its messages through the awaited <see cref="HandleClientAsync"/>
	/// loop and does not use the base read loop; the implementation is provided for
	/// interface completeness.
	/// </remarks>
	protected override Task<byte[]> ReadNextAsync(CancellationToken ct) =>
		ReadMessageAsync();

	private async Task<byte[]> ReadMessageAsync()
	{
		try
		{
			if (_serverStream == null || !_serverStream.IsConnected)
				return null;

			// Allow large messages with a sanity cap at 1GB to avoid OOM
			return await LengthPrefixedFrame
				.ReadFrameAsync(_serverStream, 1_073_741_824, _cancellationTokenSource.Token)
				.ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
			return null;
		}
		catch (IOException ioEx) when (ioEx.Message.Contains("Broken pipe") || ioEx.Message.Contains("Connection"))
		{
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

	private async Task<bool> CreateSessionAsNeeded(ClientHandshakeMessage handshake)
	{
		if (Session != null)
			return false;

		await CreateRemotingSessionAsync(handshake).ConfigureAwait(false);

		return true;
	}

	/// <inheritdoc />
	protected override void BeforeDisposeSession()
	{
		Session = null;
		// Don't dispose the connection here - just like in WebSocket implementation
		// The connection should only be disposed when the client actually disconnects
		// or when the server stops. This prevents premature disconnection.
	}

	/// <inheritdoc />
	protected override async Task<bool> SendCoreAsync(byte[] rawMessage)
	{
		if (_isDisposed || rawMessage == null || _serverStream == null)
			return false;

		using (await SendLock)
		{
			if (!_serverStream.IsConnected)
				return false;

			try
			{
				await LengthPrefixedFrame
					.WriteFrameAsync(_serverStream, rawMessage, _cancellationTokenSource.Token)
					.ConfigureAwait(false);

				return true;
			}
			catch (IOException ioEx) when (ioEx.Message.Contains("Broken pipe") || ioEx.Message.Contains("Connection"))
			{
				return false;
			}
		}
	}

	/// <inheritdoc />
	protected override void ReportSendError(Exception ex)
	{
		LastException = new NetworkException($"Failed to send message: {ex.Message}", ex);
		OnErrorOccured($"Connection {_connectionId} send error: {ex.Message}", ex);
	}

	/// <summary>
	/// Disposes the connection asynchronously.
	/// </summary>
	/// <returns>A <see cref="ValueTask"/> that represents the asynchronous dispose operation.</returns>
	public override async ValueTask DisposeAsync()
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

		// dispose the base send/receive locks
		await base.DisposeAsync().ConfigureAwait(false);

		// Notify subscribers that this connection is disposed
		// Include information about whether this was due to session end
		Disposed?.Invoke(this, EventArgs.Empty);
	}

	/// <summary>
	/// Disposes the connection synchronously.
	/// </summary>
	public void Dispose()
	{
		DisposeAsync().AsTask().Wait();
	}
}
