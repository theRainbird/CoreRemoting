using System;
using System.Runtime.Serialization;

namespace CoreRemoting.RpcMessaging
{
    [DataContract]
    [Serializable]
    public class RemoteDelegateInvocationMessage
    {
        [DataMember]
        public Guid UniqueCallKey { get; set; }
        
        [DataMember]
        public Guid HandlerKey { get; set; }
        
        [DataMember]
        public object[] DelegateArguments { get; set; }
    }
}