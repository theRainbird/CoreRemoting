using System;
using System.Runtime.Serialization;

namespace CoreRemoting
{
    [Serializable]
    public class RemotingException : Exception
    {
        public RemotingException(string message = "Remoting operation failed.", Exception innerEx = null) : 
            base(message, innerEx)
        {
        }
        
        // Without this constructor, deserialization will fail
        public RemotingException(SerializationInfo info, StreamingContext context) 
            : base(info, context)
        {
        }
    }
}