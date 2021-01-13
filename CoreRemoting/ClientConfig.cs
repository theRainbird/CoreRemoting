using System.Diagnostics.CodeAnalysis;
using CoreRemoting.Authentication;
using CoreRemoting.Channels;
using CoreRemoting.Serialization;

namespace CoreRemoting
{
    public class ClientConfig
    {
        public int ConnectionTimeout { get; set; } = 10;
        
        public int AuthenticationTimeout { get; set; } = 10;

        public int InvocationTimeout { get; set; }

        public string ServerHostName { get; set; } = "localhost";

        public int ServerPort { get; set; } = 9090;
        
        public ISerializerAdapter Serializer { get; set; }

        public int KeySize { get; set; } = 4096;

        public bool MessageEncryption { get; set; }
        
        [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global")]
        public IClientChannel Channel { get; set; }
        
        [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global")]
        public IKnownTypeProvider KnownTypeProvider { get; set; }
        
        public Credential[] Credentials { get; set; }
    }
}