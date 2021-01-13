using System;
using System.Runtime.Serialization;

namespace CoreRemoting
{
    [Serializable]
    public class RemoteInvocationException : Exception
    {
        public RemoteInvocationException(string message = "Remote invocation failed.", Exception innerEx = null) : 
            base(message, innerEx)
        {
        }
        
        // Without this constructor, deserialization will fail
        public RemoteInvocationException(SerializationInfo info, StreamingContext context) 
            : base(info, context)
        {
        }
    }
}