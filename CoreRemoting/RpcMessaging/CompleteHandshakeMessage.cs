using System;
using System.Runtime.Serialization;

namespace CoreRemoting.RpcMessaging;

/// <summary>
/// Describes a message sent from server to client to complete the handshake.
/// </summary>
[DataContract]
[Serializable]
public class CompleteHandshakeMessage
{
    /// <summary>
    /// Gets or sets a value indicating whether the authentication is required
    /// </summary>
    [DataMember]
    public bool AuthenticationRequired { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether message encryption is required
    /// </summary>
    [DataMember]
    public bool MessageEncryptionRequired { get; set; }

    /// <summary>
    /// Gets or sets the shared secret for symmetric encryption, if message encryption is enabled.
    /// </summary>
    [DataMember]
    public byte[] SharedSecret { get; set; }

    /// <summary>
    /// Gets or sets the session identity.
    /// </summary>
    [DataMember]
    public Guid SessionId { get; set; }
}