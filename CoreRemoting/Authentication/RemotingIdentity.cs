using System;
using System.Runtime.Serialization;
using System.Security.Principal;

namespace CoreRemoting.Authentication
{
    /// <summary>
    /// Identity authenticated by a CoreRemoting server.
    /// </summary>
    [DataContract]
    [Serializable]
    public class RemotingIdentity : IIdentity
    {
        /// <summary>
        /// Gets or sets the name of the identity.
        /// </summary>
        [DataMember]
        public string Name { get; set; }
        
        /// <summary>
        /// Gets or sets the optional domain or realm name of the identity.
        /// </summary>
        [DataMember]
        public string Domain { get; set; }
        
        /// <summary>
        /// Gets or sets an array of roles, the identity is member of.
        /// </summary>
        [DataMember]
        public string[] Roles { get; set; }

        /// <summary>
        /// Gets or sets a string, that describes the authentication type.
        /// </summary>
        [DataMember]
        public string AuthenticationType { get; set; }

        /// <summary>
        /// Gets or sets whether the identity was successful authenticated or not.
        /// </summary>
        [DataMember]
        public bool IsAuthenticated { get; set; }
    }
}