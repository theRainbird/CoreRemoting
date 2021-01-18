using System;
using System.Runtime.Serialization;

namespace CoreRemoting.Authentication
{
    /// <summary>
    /// Describes an authentication request message.
    /// </summary>
    [DataContract]
    [Serializable]
    [KnownType(typeof(Credential))]
    [KnownType(typeof(Credential[]))]
    public class AuthenticationRequestMessage
    {
        /// <summary>
        /// Get or sets an array of credentials for authentication.
        /// </summary>
        [DataMember]
        public Credential[] Credentials { get; set; }
    }
}