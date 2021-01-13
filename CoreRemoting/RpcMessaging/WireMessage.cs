using System;

namespace CoreRemoting.RpcMessaging
{
    [Serializable]
    public class WireMessage
    {
        public string MessageType { get; set; }
        
        public byte[] Data { get; set; }
        
        public byte[] Iv { get; set; }
        
        public bool Error { get; set; }
        
        public byte[] UniqueCallKey { get; set; }
    }
}