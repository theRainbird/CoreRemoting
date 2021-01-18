using System;
using System.Runtime.Serialization;

namespace CoreRemoting.Authentication
{
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
        /// Get or sets the authenticated identity.
        /// </summary>
        [DataMember]
        public RemotingIdentity AuthenticatedIdentity { get; set; }
    }
}