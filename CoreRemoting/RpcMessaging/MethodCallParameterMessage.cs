using System;
using System.Runtime.Serialization;

namespace CoreRemoting.RpcMessaging
{
    /// <summary>
    /// Serializable message that describes a parameter of an remote method call. 
    /// </summary>
    [DataContract]
    [Serializable]
    public class MethodCallParameterMessage
    {
        /// <summary>
        /// Gets or sets the name of the parameter.
        /// </summary>
        [DataMember]
        public string ParameterName { get; set; }
        
        /// <summary>
        /// Gets or sets the type name of the parameter.
        /// </summary>
        [DataMember]
        public string ParameterTypeName { get; set; }
        
        /// <summary>
        /// Gets or sets whether the parameter is an out parameter or not.
        /// </summary>
        [DataMember]
        public bool IsOut { get; set; }
        
        /// <summary>
        /// Gets or sets whether the parameter is null or not.
        /// </summary>
        [DataMember]
        public bool IsValueNull { get; set; }
        
        /// <summary>
        /// Gets or sets the parameter value.
        /// </summary>
        [DataMember]
        public object Value { get; set; }
    }
}