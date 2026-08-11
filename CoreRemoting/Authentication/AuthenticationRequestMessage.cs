using System;
using System.Linq;
using System.Runtime.Serialization;

namespace CoreRemoting.Authentication;

/// <summary>
/// Describes an authentication request message.
/// </summary>
[DataContract]
[Serializable]
public class AuthenticationRequestMessage
{
    /// <summary>
    /// Get or sets an array of credentials for authentication.
    /// </summary>
    [DataMember]
    public Credential[] Credentials { get; set; }

    /// <summary>
    /// Gets the value of the given credential, or null.
    /// </summary>
    /// <param name="name">Credential name, case-insensitive.</param>
    public string this[string name] => Credentials?.FirstOrDefault(c =>
        string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))?.Value;
}