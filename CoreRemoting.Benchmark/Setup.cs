using CoreRemoting.Channels;
using CoreRemoting.Channels.NamedPipe;
using CoreRemoting.Channels.Null;
using CoreRemoting.Channels.Tcp;
using CoreRemoting.Channels.Websocket;

#if NET9_0_OR_GREATER
using CoreRemoting.Channels.Quic;
#endif

namespace CoreRemoting.Benchmark;

public static class Setup
{
    public interface IScenario
    {
        IServerChannel CreateServer();
        IClientChannel CreateClient();
        bool MessageEncryption { get; }
        string ConnectionName { get; }
    }

    private class Plain<TServer, TClient> : IScenario
        where TServer : IServerChannel, new()
        where TClient : IClientChannel, new()
    {
        public IServerChannel CreateServer() => new TServer();
        public IClientChannel CreateClient() => new TClient();
        public virtual bool MessageEncryption => false;
        public string ConnectionName { get; } = "Bench" + Guid.NewGuid();
        public override string ToString() => $"{Short(typeof(TServer).Name)}_Plain";
        protected static string Short(string name) =>
        	name.Replace("ServerChannel", "").Replace("ClientChannel", "");
    }

    private sealed class Encrypted<TServer, TClient> : Plain<TServer, TClient>
        where TServer : IServerChannel, new()
        where TClient : IClientChannel, new()
    {
        public override bool MessageEncryption => true;
        public override string ToString() => $"{Short(typeof(TServer).Name)}_Secure";
    }

    public static IScenario[] Scenarios { get; } =
    [
        new Plain<NullServerChannel, NullClientChannel>(),
        new Encrypted<NullServerChannel, NullClientChannel>(),
        new Plain<NamedPipeServerChannel, NamedPipeClientChannel>(),
        new Encrypted<NamedPipeServerChannel, NamedPipeClientChannel>(),
        new Plain<TcpServerChannel, TcpClientChannel>(),
        new Encrypted<TcpServerChannel, TcpClientChannel>(),
        new Plain<WebsocketServerChannel, WebsocketClientChannel>(),
        new Encrypted<WebsocketServerChannel, WebsocketClientChannel>(),
      #if NET9_0_OR_GREATER
        new Plain<QuicServerChannel, QuicClientChannel>(),
        new Encrypted<QuicServerChannel, QuicClientChannel>(),
      #endif
    ];
}
