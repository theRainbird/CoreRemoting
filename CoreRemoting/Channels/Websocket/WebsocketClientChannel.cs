using System;
using System.Net;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using CoreRemoting.IO;

namespace CoreRemoting.Channels.Websocket;

/// <summary>
/// Client side websocket channel implementation based on System.Net.Websockets.
/// </summary>
public class WebsocketClientChannel : IClientChannel, IRawMessageTransport
{
    // note: LOH threshold is ~85 kilobytes
    private const int BufferSize = 16 * 1024;
    private static ArraySegment<byte> EmptyMessage = new ArraySegment<byte>([]);

    /// <summary>
    /// Gets or sets the URL this channel is connected to.
    /// </summary>
    public string Url { get; private set; }

    private Uri Uri { get; set; }

    private ClientWebSocket WebSocket { get; set; }

    /// <inheritdoc />
    public bool IsConnected { get; private set; }

    /// <inheritdoc />
    public IRawMessageTransport RawMessageTransport => this;

    /// <inheritdoc />
    public NetworkException LastException { get; set; }

    /// <summary>
    /// Event: fires when the channel is connected.
    /// </summary>
    public event Action Connected;

    /// <inheritdoc />
    public event Action Disconnected;

    /// <inheritdoc />
    public event Action<byte[]> ReceiveMessage;

    /// <inheritdoc />
    public event Action<string, Exception> ErrorOccured;

    /// <inheritdoc />
    public void Init(IRemotingClient client)
    {
        Url =
            "ws://" +
            client.Config.ServerHostName + ":" +
            Convert.ToString(client.Config.ServerPort) +
            "/rpc";

        Uri = new Uri(Url);

        // note: Nagle is disabled by default on NetCore, see
        // https://github.com/dotnet/runtime/discussions/81175
        WebSocket = new ClientWebSocket();
        WebSocket.Options.Cookies = new CookieContainer();
        WebSocket.Options.Cookies.Add(new Cookie(
            name: "MessageEncryption",
            value: client.MessageEncryption ? "1" : "0",
            path: Uri.LocalPath,
            domain: Uri.Host));

        if (client.MessageEncryption)
        {
            WebSocket.Options.Cookies.Add(new Cookie(
                name: "ShakeHands",
                value: Convert.ToBase64String(client.PublicKey),
                path: Uri.LocalPath,
                domain: Uri.Host));
        }
    }

    /// <inheritdoc />
    public void Connect()
    {
        ConnectTask = ConnectTask ?? Task.Factory.StartNew(async () =>
        {
            await WebSocket.ConnectAsync(
                new Uri(Url), CancellationToken.None)
                    .ConfigureAwait(false);

            IsConnected = true;
            Connected?.Invoke();

            await WebSocket.SendAsync(EmptyMessage, 
                WebSocketMessageType.Binary, true, CancellationToken.None)
                    .ConfigureAwait(false);

            _ = StartListening();
        });

        ConnectTask.GetAwaiter().GetResult();
    }

    private Task ConnectTask { get; set; }

    private async Task StartListening()
    {
        var buffer = new byte[BufferSize];
        var segment = new ArraySegment<byte>(buffer);
        var webSocket = WebSocket;

        try
        {
            while (webSocket.State == WebSocketState.Open)
            {
                var ms = new SmallBlockMemoryStream();
                while (true)
                {
                    var result = await webSocket.ReceiveAsync(
                        segment, CancellationToken.None)
                            .ConfigureAwait(false);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure,
                            string.Empty, CancellationToken.None)
                                .ConfigureAwait(false);

                        Disconnected?.Invoke();
                    }
                    else
                    {
                        ms.Write(buffer, 0, result.Count);
                    }

                    if (result.EndOfMessage)
                        break;
                }

                if (ms.Length > 0)
                {
                    // flush received websocket message
                    var message = new byte[(int)ms.Length];
                    ms.Position = 0;
                    ms.Read(message, 0, message.Length);
                    ReceiveMessage?.Invoke(message);
                }
            }
        }
        catch (Exception ex)
        {
            LastException = ex as NetworkException ??
                new NetworkException(ex.Message, ex);

            ErrorOccured?.Invoke(ex.Message, ex);
            Disconnected?.Invoke();
        }
        finally
        {
            webSocket?.Dispose();
        }
    }

    /// <inheritdoc />
    public bool SendMessage(byte[] rawMessage)
    {
        try
        {
            var segment = new ArraySegment<byte>(rawMessage);
            WebSocket.SendAsync(segment, 
                WebSocketMessageType.Binary, true, CancellationToken.None)
                    .GetAwaiter().GetResult();

            return true;
        }
        catch (Exception ex)
        {
            LastException = ex as NetworkException ??
                new NetworkException(ex.Message, ex);

            ErrorOccured?.Invoke(ex.Message, ex);
            return false;
        }
    }

    private Task DisconnectTask { get; set; }

    /// <inheritdoc />
    public void Disconnect()
    {
        DisconnectTask = DisconnectTask ?? Task.Factory.StartNew(async () =>
        {
            await WebSocket.CloseAsync(
                WebSocketCloseStatus.NormalClosure, "Ok", CancellationToken.None)
                    .ConfigureAwait(false);

            IsConnected = false;
            Disconnected?.Invoke();
        });
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (WebSocket == null)
            return;

        if (IsConnected)
            Disconnect();

        var task = DisconnectTask;
        if (task != null)
            task.GetAwaiter()
                .GetResult();

        WebSocket.Dispose();
        WebSocket = null;
    }
}
