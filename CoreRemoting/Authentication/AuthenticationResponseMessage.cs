using System;
using System.Runtime.Serialization;
using Castle.Windsor.Diagnostics;

namespace CoreRemoting.Authentication;

/// <summary>
/// Describes an authentication response message.
/// </summary>
[DataContract]
[Serializable]
public class AuthenticationResponseMessage
{
    /// <summary>
    /// Gets or sets whether authentication was successful.
    /// </summary>
    [DataMember]
    public bool IsAuthenticated { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the authentication was completed.
    /// </summary>
    [DataMember]
    public bool IsCompleted { get; set; } = true;

    /// <summary>
    /// Get or sets the authenticated identity.
    /// </summary>
    [DataMember]
    public RemotingIdentity AuthenticatedIdentity { get; set; }

    /// <summary>
    /// Get or sets optional authentication response parameters used by advanced protocols.
    /// </summary>
    [DataMember]
    public AuthenticationResponseParameter[] Parameters { get; set; }
}
