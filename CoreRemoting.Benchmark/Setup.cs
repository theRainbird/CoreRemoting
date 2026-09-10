using System;
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
    public interface ICase
    {
        IServerChannel CreateServer();
        IClientChannel CreateClient();
        bool Encrypted { get; }
        string ConnectionName { get; }
    }

    // --- Two-parameter case: plain -------------------------------------------

    private class Case<TServer, TClient> : ICase
        where TServer : IServerChannel, new()
        where TClient : IClientChannel, new()
    {
        public IServerChannel CreateServer() => new TServer();
        public IClientChannel CreateClient() => new TClient();
        public virtual bool Encrypted => false;
        public string ConnectionName { get; } = "Bench" + Guid.NewGuid();
        public override string ToString() => $"{Short(typeof(TServer).Name)}_Plain";
        protected static string Short(string name) =>
        	name.Replace("ServerChannel", "").Replace("ClientChannel", "");
    }

    // --- Three-parameter case: encrypted -------------------------------------

    private sealed class Case<TServer, TClient, TSecure> : Case<TServer, TClient>
        where TServer : IServerChannel, new()
        where TClient : IClientChannel, new()
    {
        public override bool Encrypted => true;
        public override string ToString() => $"{Short(typeof(TServer).Name)}_Secure";
    }

    // --- Dummy marker for the third generic parameter ------------------------

    private sealed class Secure;

    // --- Scenarios сatalog ---------------------------------------------------

    public static ICase[] Scenarios { get; } =
    [
        new Case<NullServerChannel, NullClientChannel>(),
        new Case<NullServerChannel, NullClientChannel, Secure>(),
        new Case<NamedPipeServerChannel, NamedPipeClientChannel>(),
        new Case<NamedPipeServerChannel, NamedPipeClientChannel, Secure>(),
        new Case<TcpServerChannel, TcpClientChannel>(),
        new Case<TcpServerChannel, TcpClientChannel, Secure>(),
        new Case<WebsocketServerChannel, WebsocketClientChannel>(),
        new Case<WebsocketServerChannel, WebsocketClientChannel, Secure>(),
      #if NET9_0_OR_GREATER
        new Case<QuicServerChannel, QuicClientChannel>(),
        new Case<QuicServerChannel, QuicClientChannel, Secure>(),
      #endif
    ];
}
