using System;
using System.Diagnostics.CodeAnalysis;
using CoreRemoting.Authentication;
using CoreRemoting.Channels;
using CoreRemoting.DependencyInjection;
using CoreRemoting.Serialization;

namespace CoreRemoting
{
    [SuppressMessage("ReSharper", "RedundantDefaultMemberInitializer")]
    public class ServerConfig
    {
        [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global")]
        public string HostName { get; set; } = "localhost";

        public int NetworkPort { get; set; } = 9090;

        [SuppressMessage("ReSharper", "AutoPropertyCanBeMadeGetOnly.Global")] 
        public int KeySize { get; set; } = 4096;
        
        [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global")] 
        public bool MessageEncryption { get; set; } = true;
        
        public ISerializerAdapter Serializer { get; set; }

        public IDependencyInjectionContainer DependencyInjectionContainer { get; set; }

        public Action<IDependencyInjectionContainer> RegisterServicesAction { get; set; }
        
        [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global")] 
        public ISessionRepository SessionRepository { get; set; }
        
        [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global")] 
        public IServerChannel Channel { get; set; }
        
        [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global")] 
        public IKnownTypeProvider KnownTypeProvider { get; set; }
        
        public bool AuthenticationRequired { get; set; } = false;
        
        public IAuthenticationProvider AuthenticationProvider { get; set; }
        
        public string UniqueServerInstanceName { get; set; }
    }
}