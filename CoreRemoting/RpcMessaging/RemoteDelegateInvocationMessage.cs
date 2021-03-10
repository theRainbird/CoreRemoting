using System;
using System.Runtime.Serialization;

namespace CoreRemoting.RpcMessaging
{
    /// <summary>
    /// Serializable message that describes the invocation of a remote delegate.
    /// </summary>
    [DataContract]
    [Serializable]
    public class RemoteDelegateInvocationMessage
    {
        /// <summary>
        /// Gets or sets an unique key to correlate RPC calls.
        /// </summary>
        [DataMember]
        public Guid UniqueCallKey { get; set; }
        
        /// <summary>
        /// Gets or sets a unique handler key to identify the remote delegate. 
        /// </summary>
        [DataMember]
        public Guid HandlerKey { get; set; }
        
        /// <summary>
        /// Gets or sets an array of arguments that should be passed as parameters to the remote delegate.
        /// </summary>
        [DataMember]
        public object[] DelegateArguments { get; set; }
    }
}