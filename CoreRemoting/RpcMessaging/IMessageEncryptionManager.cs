namespace CoreRemoting.RpcMessaging
{
    public interface IMessageEncryptionManager
    {
        WireMessage CreateWireMessage(
            string messageType,
            byte[] serializedMessage,
            byte[] sharedSecret = null,
            bool error = false,
            byte[] uniqueCallKey = null);

        byte[] GetDecryptedMessageData(
            WireMessage message,
            byte[] sharedSecret = null);
    }
}