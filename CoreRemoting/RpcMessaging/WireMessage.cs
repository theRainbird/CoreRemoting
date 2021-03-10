using System;

namespace CoreRemoting.RpcMessaging
{
    /// <summary>
    /// Serializable message to transport RPC invocation details and their results over the wire. 
    /// </summary>
    [Serializable]
    public class WireMessage
    {
        /// <summary>
        /// Gets or sets the type of the message.
        /// </summary>
        public string MessageType { get; set; }
        
        /// <summary>
        /// Gets or sets the raw data of the message content and its RSA signatures (only if message encryption is enabled).
        /// </summary>
        public byte[] Data { get; set; }
        
        /// <summary>
        /// Gets or sets the initialization vector as byte array (only needed if message encryption is enabled).
        /// </summary>
        public byte[] Iv { get; set; }
        
        /// <summary>
        /// Gets or sets whether this message contains error data.
        /// </summary>
        public bool Error { get; set; }
        
        /// <summary>
        /// Gets or sets an unique key to correlate RPC calls.
        /// </summary>
        public byte[] UniqueCallKey { get; set; }
    }
}