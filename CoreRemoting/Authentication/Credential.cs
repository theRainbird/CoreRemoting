using System;
using System.Runtime.Serialization;

namespace CoreRemoting.Authentication;

/// <summary>
/// Describes an authentication credential.
/// </summary>
[DataContract]
[Serializable]
public class Credential
{
    /// <summary>
    /// Gets or sets the name of the credential (e.g. "password").
    /// </summary>
    [DataMember]
    public string Name { get; set; }

    /// <summary>
    /// Get or sets the value of the credential (e.g. "secret").
    /// </summary>
    [DataMember]
    public string Value { get; set; }
}