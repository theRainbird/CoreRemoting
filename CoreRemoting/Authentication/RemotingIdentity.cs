using System;
using System.Runtime.Serialization;
using System.Security.Principal;

namespace CoreRemoting.Authentication
{
    [DataContract]
    [Serializable]
    public class RemotingIdentity : IIdentity
    {
        [DataMember]
        public string Name { get; set; }
        
        [DataMember]
        public string Domain { get; set; }
        
        [DataMember]
        public string[] Roles { get; set; }

        [DataMember]
        public string AuthenticationType { get; set; }

        [DataMember]
        public bool IsAuthenticated { get; set; }
    }
}