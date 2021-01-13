using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace CoreRemoting.RpcMessaging
{
    [DataContract]
    [Serializable]
    public class MethodCallMessage
    {
        [DataMember]
        public string ServiceName { get; set; }
        
        [DataMember]
        public string MethodName { get; set; }
        
        [DataMember]
        public MethodCallParameterMessage[] Parameters { get; set; }
        
        [DataMember]
        public CallContextEntry[] CallContextSnapshot { get; set; }
    }
}