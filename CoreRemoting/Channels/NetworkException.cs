using System;
using System.Runtime.Serialization;

namespace CoreRemoting.Channels
{
    [Serializable]
    public class NetworkException : Exception
    {
        public NetworkException(string message = "Network operation failed.", Exception innerEx = null) : 
            base(message, innerEx)
        {
        }
        
        // Without this constructor, deserialization will fail
        public NetworkException(SerializationInfo info, StreamingContext context) 
            : base(info, context)
        {
        }
    }
}