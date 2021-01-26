using System;
using System.Runtime.Serialization;

namespace CoreRemoting.RpcMessaging
{
    /// <summary>
    /// Serializable message that describes an out parameter.
    /// </summary>
    [DataContract]
    [Serializable]
    public class MethodCallOutParameterMessage
    {
        /// <summary>
        /// Gets or sets the name of the parameter.
        /// </summary>
        [DataMember]
        public string ParameterName { get; set; }
        
        /// <summary>
        /// Gets or sets the out value of the parameter.
        /// </summary>
        [DataMember]
        public object OutValue { get; set; }
        
        /// <summary>
        /// Gets or sets whether the out value is null.
        /// </summary>
        [DataMember]
        public bool IsOutValueNull { get; set; }
    }
}