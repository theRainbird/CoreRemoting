using System;
using System.Runtime.Serialization;

namespace CoreRemoting
{
    /// <summary>
    /// Exception to be thrown, if a remoting operation has been failed.
    /// </summary>
    [Serializable]
    public class RemotingException : Exception
    {
        /// <summary>
        /// Creates a new instance of the RemotingException class.
        /// </summary>
        /// <param name="message">Error message</param>
        /// <param name="innerEx">Optional inner exception</param>
        public RemotingException(string message = "Remoting operation failed.", Exception innerEx = null) : 
            base(message, innerEx)
        {
        }
        
        /// <summary>
        /// Without this constructor, deserialization will fail. 
        /// </summary>
        /// <param name="info">Serialization info</param>
        /// <param name="context">Streaming context</param>
        public RemotingException(SerializationInfo info, StreamingContext context) 
            : base(info, context)
        {
        }
    }
}