using System;
using System.Runtime.Serialization;

namespace CoreRemoting.Authentication;

/// <summary>
/// Describes an authentication response message parameter.
/// </summary>
[DataContract]
[Serializable]
public class AuthenticationResponseParameter
{
    /// <summary>
    /// Gets or sets the name of the parameter (e.g. "salt").
    /// </summary>
    [DataMember]
    public string Name { get; set; }

    /// <summary>
    /// Get or sets the value of the parameter (e.g. "pepper").
    /// </summary>
    [DataMember]
    public string Value { get; set; }
}