using System;
using System.Runtime.Serialization;

namespace CoreRemoting.RpcMessaging
{
    [DataContract]
    [Serializable]
    public class MethodCallParameterMessage
    {
        [DataMember]
        public string ParameterName { get; set; }
        
        [DataMember]
        public string ParameterTypeName { get; set; }
        
        [DataMember]
        public bool IsOut { get; set; }
        
        [DataMember]
        public bool IsValueNull { get; set; }
        
        [DataMember]
        public object Value { get; set; }
    }
}