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
    private readonly CancellationTokenSource _cancellationTokenSource;
    private Task _receivingTask;
    private bool _isConnected;
    private readonly AsyncLock _disposeLock;

    /// <summary>
    /// Creates a new instance of the NamedPipeClientChannel class.
    /// </summary>
    public NamedPipeClientChannel()
    {
        _cancellationTokenSource = new CancellationTokenSource();
        _disposeLock = new AsyncLock();
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

        if (_isConnected)
            return;

        try
        {
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
            await _pipeClient.ConnectAsync(connectionTimeoutMs);
            
            if (!_pipeClient.IsConnected)
                throw new InvalidOperationException("Failed to establish named pipe connection.");
                
            _isConnected = true;

            // Start receiving messages after connection is established
            _receivingTask = Task.Run(ReceiveMessagesAsync, _cancellationTokenSource.Token);

            // Send handshake message immediately after connection
            await SendHandshakeAsync();
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
            await SendMessageAsync(new byte[0]);
        }
        catch (Exception ex)
        {
            // Log handshake error but don't fail connection
            Console.Error.WriteLine($"Handshake failed: {ex.Message}");
        }
    }

    private async Task ReceiveMessagesAsync()
    {
        try
        {
            // Process messages while connected
            while (!_cancellationTokenSource.Token.IsCancellationRequested && _isConnected && _pipeClient.IsConnected)
            {
                var messageData = await ReadMessageAsync();
                if (messageData == null)
                    break;

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

    private async Task<byte[]> ReadMessageAsync()
    {
        if (_pipeClient == null || !_pipeClient.IsConnected)
            return null;

        try
        {
            // Read message length (4 bytes)
            var lengthBuffer = new byte[4];
            var bytesRead = await _pipeClient.ReadAsync(lengthBuffer, 0, 4, _cancellationTokenSource.Token);
            
            if (bytesRead != 4)
                return null;

            var messageLength = BitConverter.ToInt32(lengthBuffer, 0);
            
            if (messageLength < 0 || messageLength > 1024 * 1024) // 1MB max message size
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
                var read = await _pipeClient.ReadAsync(messageBuffer, totalBytesRead, bytesToRead, _cancellationTokenSource.Token);
                
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
            if (!_isConnected)
                return;

            _isConnected = false;
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
                    _pipeClient = null;
                }
            }
            catch
            {
                // Ignore disposal errors
            }
        }
    }

    /// <summary>
    /// Gets whether the connection is established or not.
    /// </summary>
    public bool IsConnected => _isConnected && _pipeClient?.IsConnected == true;

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
        if (!_isConnected || _pipeClient == null || rawMessage == null)
            return false;

        try
        {
            // Check if stream is still connected
            if (!_pipeClient.IsConnected)
                return false;

            // Send message length (4 bytes)
            var lengthBuffer = BitConverter.GetBytes(rawMessage.Length);
            await _pipeClient.WriteAsync(lengthBuffer, 0, 4, _cancellationTokenSource.Token);

            // Send message content
            await _pipeClient.WriteAsync(rawMessage, 0, rawMessage.Length, _cancellationTokenSource.Token);
            await _pipeClient.FlushAsync(_cancellationTokenSource.Token);

            return true;
        }
        catch (IOException ioEx) when (ioEx.Message.Contains("Broken pipe") || ioEx.Message.Contains("Connection"))
        {
            // Normal pipe disconnection scenario
            return false;
        }
        catch (Exception ex)
        {
            LastException = new NetworkException($"Failed to send message: {ex.Message}", ex);
            ErrorOccured?.Invoke($"Send error: {ex.Message}", ex);
            return false;
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
        await DisconnectAsync();
        _cancellationTokenSource.Dispose();
        _disposeLock.Dispose();
    }
}