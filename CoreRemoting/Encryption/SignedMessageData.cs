using System;

namespace CoreRemoting.Encryption
{
    /// <summary>
    /// Container for raw message data and its RSA signature.
    /// </summary>
    [Serializable]
    public class SignedMessageData
    {
        /// <summary>
        /// Gets or sets the unencrypted raw message data.
        /// </summary>
        public byte[] MessageRawData { get; set; }
        
        /// <summary>
        /// Get or sets the RSA signature.
        /// </summary>
        public byte[] Signature { get; set; }
    }
}