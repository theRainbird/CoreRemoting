using System;
using System.Runtime.Serialization;

namespace CoreRemoting.RpcMessaging
{
    [DataContract]
    [Serializable]
    public class MethodCallOutParameterMessage
    {
        [DataMember]
        public string ParameterName { get; set; }
        
        [DataMember]
        public object OutValue { get; set; }
        
        [DataMember]
        public bool IsOutValueNull { get; set; }
    }
}