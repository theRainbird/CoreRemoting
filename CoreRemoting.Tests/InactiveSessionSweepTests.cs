using System;
using System.Linq;
using System.Threading;
using CoreRemoting.DependencyInjection;
using CoreRemoting.Encryption;
using CoreRemoting.Serialization;
using CoreRemoting.Serialization.Bson;
using CoreRemoting.Tests.Tools;
using Xunit;

namespace CoreRemoting.Tests;

public class InactiveSessionSweepTests
{
    private static int _nextPort = 9299;

    /// <summary>
    /// Serializer that fails while a session is closed: with message encryption the "session_closed" wire message
    /// wraps an empty payload in <see cref="SignedMessageData"/>, which is serialized outside of any try/catch
    /// in <see cref="RemotingSession.DisposeAsync"/>.
    /// </summary>
    private class FailingOnSessionClosedSerializerAdapter : ISerializerAdapter
    {
        private readonly BsonSerializerAdapter _innerSerializer = new();

        public bool EnvelopeNeededForParameterSerialization => _innerSerializer.EnvelopeNeededForParameterSerialization;

        public byte[] Serialize<T>(T graph) => Serialize(typeof(T), graph);

        public byte[] Serialize(Type type, object graph)
        {
            if (graph is SignedMessageData { MessageRawData.Length: 0 })
                throw new InvalidOperationException("Simulated serializer failure while closing a session.");

            return _innerSerializer.Serialize(type, graph);
        }

        public T Deserialize<T>(byte[] rawData) => _innerSerializer.Deserialize<T>(rawData);

        public object Deserialize(Type type, byte[] rawData) => _innerSerializer.Deserialize(type, rawData);
    }

    [Fact]
    public void Sweep_should_remove_all_inactive_sessions_and_keep_the_server_alive_if_closing_a_session_throws()
    {
        var port = Interlocked.Increment(ref _nextPort);

        using var server = new RemotingServer(new ServerConfig
        {
            NetworkPort = port,
            MessageEncryption = true,
            Serializer = new FailingOnSessionClosedSerializerAdapter(),
            InactiveSessionSweepInterval = 1,
            MaximumSessionInactivityTime = 0,
            RegisterServicesAction = container =>
                container.RegisterService<ITestService, TestService>(ServiceLifetime.Singleton)
        });
        server.Start();

        var clients = Enumerable.Range(0, 2)
            .Select(_ =>
            {
                var client = new RemotingClient(new ClientConfig
                {
                    ServerPort = port,
                    MessageEncryption = true,
                    Serializer = new BsonSerializerAdapter(),
                    ConnectionTimeout = 0
                });
                client.Connect();
                Assert.Equal("hello", client.CreateProxy<ITestService>().Echo("hello"));
                return client;
            })
            .ToList();

        // Both sessions are inactive for more than 0 minutes, so the next sweep (every second) removes them.
        var timeout = DateTime.UtcNow.AddSeconds(10);
        while (server.SessionRepository.Sessions.Any() && DateTime.UtcNow < timeout)
        {
            Thread.Sleep(100);
        }

        Assert.Empty(server.SessionRepository.Sessions);

        clients.ForEach(client =>
        {
            // The server already closed the sessions, so don't wait for a goodbye answer.
            client.Disconnect(quiet: true);
            client.Dispose();
        });
    }
}
