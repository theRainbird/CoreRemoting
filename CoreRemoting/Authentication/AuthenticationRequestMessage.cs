using System;
using System.Runtime.Serialization;

namespace CoreRemoting.Authentication
{
    [DataContract]
    [Serializable]
    [KnownType(typeof(Credential))]
    [KnownType(typeof(Credential[]))]
    public class AuthenticationRequestMessage
    {
        [DataMember]
        public Credential[] Credentials { get; set; }
    }
}