using System;
using System.Threading.Tasks;

namespace CoreRemoting.Tests.Tools
{
    public static class FakeTransport
    {
        public static event Action<byte[]> ClientMessageReceived;

        public static event Action<byte[]> ServerMessageReceived;

        public static void SendMessageToClient(byte[] message)
        {
            Task.Run(() => ClientMessageReceived?.Invoke(message));
        }

        public static void SendMessageToServer(byte[] message)
        {
            Task.Run(() => ServerMessageReceived?.Invoke(message));
        }
    }
}