using System;
using System.Runtime.Serialization;

namespace CoreRemoting.Authentication
{
    [DataContract]
    [Serializable]
    public class AuthenticationResponseMessage
    {
        [DataMember]
        public bool IsAuthenticated { get; set; }
        
        [DataMember]
        public RemotingIdentity AuthenticatedIdentity { get; set; }
    }
}