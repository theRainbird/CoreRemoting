using System;
using System.Runtime.Serialization;

namespace CoreRemoting
{
    /// <summary>
    /// Exception to be thrown, if remote method invocation has been failed.
    /// </summary>
    [Serializable]
    public class RemoteInvocationException : Exception
    {
        /// <summary>
        /// Creates a new instance of the RemoteInvocationException class.
        /// </summary>
        /// <param name="message">Error message</param>
        /// <param name="innerEx">Optional inner exception</param>
        public RemoteInvocationException(string message = "Remote invocation failed.", Exception innerEx = null) : 
            base(message, innerEx)
        {
        }
        
        /// <summary>
        /// Without this constructor, deserialization will fail. 
        /// </summary>
        /// <param name="info">Serialization info</param>
        /// <param name="context">Streaming context</param>
        public RemoteInvocationException(SerializationInfo info, StreamingContext context) 
            : base(info, context)
        {
        }
    }
}