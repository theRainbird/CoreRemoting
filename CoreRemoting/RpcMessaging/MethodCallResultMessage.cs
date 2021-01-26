using System;
using System.Runtime.Serialization;

namespace CoreRemoting.RpcMessaging
{
    /// <summary>
    /// Serializable message that describes the result of a remote method call.
    /// </summary>
    [DataContract]
    [Serializable]
    public class MethodCallResultMessage
    {
        /// <summary>
        /// Gets or sets the return value of the invoked method.
        /// </summary>
        [DataMember]
        public object ReturnValue { get; set; }
        
        /// <summary>
        /// Gets or sets whether the return value is null or not.
        /// </summary>
        [DataMember]
        public bool IsReturnValueNull { get; set; }
        
        /// <summary>
        /// Gets or sets an array of out parameters.
        /// </summary>
        [DataMember]
        public MethodCallOutParameterMessage[] OutParameters { get; set; } 
        
        /// <summary>
        /// Gets or sets a snapshot of the call context that flows from server back to the client. 
        /// </summary>
        [DataMember]
        public CallContextEntry[] CallContextSnapshot { get; set; }
    }
}