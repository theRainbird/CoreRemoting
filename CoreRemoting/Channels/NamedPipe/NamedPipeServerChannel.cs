using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace CoreRemoting.Channels.NamedPipe;

/// <summary>
/// Simple synchronous Named Pipe server.
/// </summary>
public class NamedPipeServerChannel : IServerChannel
{
	private IRemotingServer _remotingServer;
	private readonly ConcurrentDictionary<string, SimpleNamedPipeConnection> _connections;
	private SimpleNamedPipeServer _pipeServer;
	private readonly CancellationTokenSource _cancellationTokenSource;
	private Task _listeningTask;
	private bool _isListening;

	/// <summary>
	/// Creates a new instance of the NamedPipeServerChannel class.
	/// </summary>
	public NamedPipeServerChannel()
	{
		_connections = new ConcurrentDictionary<string, SimpleNamedPipeConnection>();
		_cancellationTokenSource = new CancellationTokenSource();
	}

	/// <summary>
	/// Initializes the channel.
	/// </summary>
	/// <param name="server">CoreRemoting server</param>
	public void Init(IRemotingServer server)
	{
		_remotingServer = server ?? throw new ArgumentNullException(nameof(server));
	}

	/// <summary>
	/// Start listening for client requests.
	/// </summary>
	public void StartListening()
	{
		if (_remotingServer == null)
			throw new InvalidOperationException("Channel is not initialized.");

		if (_isListening)
			return;

		_isListening = true;

		var pipeName = GetPipeName();
		_pipeServer = new SimpleNamedPipeServer(pipeName);
		_pipeServer.Start();

		_listeningTask = Task.Run(AcceptClientsAsync, _cancellationTokenSource.Token);
	}

	/// <summary>
	/// Stop listening for client requests.
	/// </summary>
	public void StopListening()
	{
		if (!_isListening)
			return;

		_isListening = false;
		_cancellationTokenSource.Cancel();

		try
		{
			_listeningTask?.Wait(TimeSpan.FromSeconds(5));
		}
		catch (AggregateException)
		{
			// Expected when cancelling
		}

		// Close server
		_pipeServer?.Stop();
		_pipeServer = null;

		// Close all existing connections
		foreach (var connection in _connections.Values)
		{
			connection.DisposeAsync().AsTask().Wait();
		}

		_connections.Clear();
	}

	/// <summary>
	/// Gets whether the channel is listening or not.
	/// </summary>
	public bool IsListening => _isListening;

	private async Task AcceptClientsAsync()
	{
		while (!_cancellationTokenSource.Token.IsCancellationRequested)
		{
			try
			{
				var serverStream = await _pipeServer.AcceptClientAsync().ConfigureAwait(false);
				if (serverStream != null)
				{
					var connectionId = Guid.NewGuid().ToString();
					var connection = new SimpleNamedPipeConnection(connectionId, serverStream, _remotingServer);
					_connections.TryAdd(connectionId, connection);

					// Subscribe to connection disconnection to remove from collection
					// This follows the same pattern as WebsocketServerChannel
					connection.Disconnected += () =>
					{
						_connections.TryRemove(connectionId, out _);
						_ = connection.DisposeAsync();
					};

					// Start handling this client
					_ = Task.Run(() => connection.HandleClientAsync());
				}
			}
			catch (OperationCanceledException)
			{
				// Expected when stopping
				break;
			}
			catch (Exception ex)
			{
				// Log error but continue listening
				Console.Error.WriteLine($"Error accepting named pipe connection: {ex.Message}");
			}
		}
	}

	private string GetPipeName()
	{
		// Use configured pipe name or default
		return _remotingServer.Config?.ChannelConnectionName ?? "CoreRemoting";
	}

	/// <summary>
	/// Stops listening and frees managed resources.
	/// </summary>
	public async ValueTask DisposeAsync()
	{
		StopListening();
		await Task.CompletedTask;
		_cancellationTokenSource.Dispose();
	}
}