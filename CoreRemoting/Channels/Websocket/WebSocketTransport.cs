using System;
using System.Net.WebSockets;
using CoreRemoting.IO;
using System.Threading.Tasks;
using System.Threading;

namespace CoreRemoting.Channels.Websocket
{
    /// <summary>
    /// Abstract web socket transport for reading and writing messages.
    /// </summary>
    public abstract class WebSocketTransport : IRawMessageTransport
    {
        /// <summary>
        /// Handshake cookies: message encryption flag.
        /// </summary>
        protected const string MessageEncryptionCookie = "MessageEncryption";

        /// <summary>
        /// Handshake cookies: client public key.
        /// </summary>
        protected const string ClientPublicKeyCookie = "ShakeHands";

        /// <summary>
        /// Buffer size to read incoming messages.
        /// Note: LOH threshold is ~85 kilobytes
        /// </summary>
        protected const int BufferSize = 16 * 1024;

        /// <summary>
        /// Empty message for ping, handshake, etc.
        /// </summary>
        protected static ArraySegment<byte> EmptyMessage = new([]);

        /// <inheritdoc />
        public NetworkException LastException { get; set; }

        /// <summary>
        /// Event: fires when the channel is connected.
        /// </summary>
        public event Action Connected;

        /// <summary>
        /// Fires the <see cref="Connected"/> event.
        /// </summary>
        protected void OnConnected() =>
            Connected?.Invoke();

        /// <inheritdoc />
        public event Action Disconnected;

        /// <summary>
        /// Fires the <see cref="Disconnected"/> event.
        /// </summary>
        protected void OnDisconnected() =>
            Disconnected?.Invoke();

        /// <inheritdoc />
        public event Action<byte[]> ReceiveMessage;

        /// <inheritdoc />
        public event Action<string, Exception> ErrorOccured;

        /// <summary>
        /// Web socket used to read and write messages.
        /// </summary>
        protected abstract WebSocket WebSocket { get; }

        /// <summary>
        /// Starts listening for the incoming messages.
        /// </summary>
        public virtual Guid StartListening()
        {
            _ = ReadIncomingMessages();
            return Guid.Empty;
        }

        /// <summary>
        /// Reads the incoming websocket messages
        /// and fires the <see cref="ReceiveMessage"/> event.
        /// </summary>
        private async Task ReadIncomingMessages()
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

                ErrorOccured?.Invoke(ex.Message, LastException);
                Disconnected?.Invoke();
            }
            finally
            {
                webSocket?.Dispose();
            }
        }

        /// <inheritdoc />
        public async Task<bool> SendMessageAsync(byte[] rawMessage)
        {
            try
            {
                await WebSocket.SendAsync(
                    new ArraySegment<byte>(rawMessage),
                        WebSocketMessageType.Binary, true, CancellationToken.None)
                            .ConfigureAwait(false);

                return true;
            }
            catch (Exception ex)
            {
                LastException = ex as NetworkException ??
                    new NetworkException(ex.Message, ex);

                ErrorOccured?.Invoke(ex.Message, LastException);
                return false;
            }
        }
    }
}
