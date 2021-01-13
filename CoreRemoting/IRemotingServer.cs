using System;
using CoreRemoting.Authentication;
using CoreRemoting.DependencyInjection;
using CoreRemoting.RpcMessaging;
using CoreRemoting.Serialization;

namespace CoreRemoting
{
    public interface IRemotingServer : IDisposable
    {
        IDependencyInjectionContainer ServiceRegistry { get; }
        
        ISerializerAdapter Serializer { get; }
        
        MethodCallMethodCallMessageBuilder MethodCallMethodCallMessageBuilder { get; }
        
        IMessageEncryptionManager MessageEncryptionManager { get; }
        
        ISessionRepository SessionRepository { get; }
        
        IKnownTypeProvider KnownTypeProvider { get; }
        
        ServerConfig Config { get; }
        
        void Start();
        
        void Stop();

        bool Authenticate(Credential[] credentials, out RemotingIdentity authenticatedIdentity);
        
        event EventHandler<ServerRpcContext> BeforeCall;
        
        event EventHandler<ServerRpcContext> AfterCall;
    }
}