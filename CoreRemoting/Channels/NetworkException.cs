using System;
using System.Runtime.Serialization;

namespace CoreRemoting.Channels
{
    /// <summary>
    /// Exception that is thrown when a network operation is failed.
    /// </summary>
    [Serializable]
    public class NetworkException : Exception
    {
        /// <summary>
        /// Creates a new instance of the NetworkException class.
        /// </summary>
        /// <param name="message">Error message</param>
        /// <param name="innerEx">Optional inner exception</param>
        public NetworkException(string message = "Network operation failed.", Exception innerEx = null) : 
            base(message, innerEx)
        {
        }
        
        /// <summary>
        /// Without this constructor, deserialization will fail. 
        /// </summary>
        /// <param name="info">Serialization info</param>
        /// <param name="context">Streaming context</param>
        public NetworkException(SerializationInfo info, StreamingContext context) 
            : base(info, context)
        {
        }
    }
}