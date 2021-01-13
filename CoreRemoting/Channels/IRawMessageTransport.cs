using System;

namespace CoreRemoting.Channels
{
    public interface IRawMessageTransport
    {
        event Action<byte[]> ReceiveMessage;

        event Action<string, Exception> ErrorOccured;
        
        NetworkException LastException { get; set; }
        
        void SendMessage(byte[] rawMessage);
    }
}