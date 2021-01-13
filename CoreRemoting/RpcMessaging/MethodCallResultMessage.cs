using System;
using System.Runtime.Serialization;

namespace CoreRemoting.RpcMessaging
{
    [DataContract]
    [Serializable]
    public class MethodCallResultMessage
    {
        [DataMember]
        public object ReturnValue { get; set; }
        
        [DataMember]
        public bool IsReturnValueNull { get; set; }
        
        [DataMember]
        public MethodCallOutParameterMessage[] OutParameters { get; set; } 
        
        [DataMember]
        public CallContextEntry[] CallContextSnapshot { get; set; }
    }
}