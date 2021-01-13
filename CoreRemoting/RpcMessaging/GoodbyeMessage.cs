using System;
using System.Runtime.Serialization;

namespace CoreRemoting.RpcMessaging
{
    [DataContract]
    [Serializable]
    public class GoodbyeMessage
    {
        [DataMember]
        public Guid SessionId { get; set; }
    }
}