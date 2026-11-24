using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreRemoting.Channels;
using CoreRemoting.Channels.Null;
using CoreRemoting.Toolbox;
using Xunit;
using Xunit.Abstractions;

namespace CoreRemoting.Tests;

public class RpcTests_NullChannel : RpcTests
{
    protected override IServerChannel ServerChannel => new NullServerChannel();

    protected override IClientChannel ClientChannel => new NullClientChannel();

    public RpcTests_NullChannel(ServerFixture fixture, ITestOutputHelper helper) : base(fixture, helper)
    {
    }

    [Fact]
    public void NullMessageQueue_cannot_connect_when_no_listener_is_registered()
    {
        Assert.Throws<Exception>(() => NullMessageQueue.Connect("123"));
    }

    [Fact]
    public void NullMessageQueue_connects_when_a_listener_is_registered()
    {
        // server endpoint
        NullMessageQueue.StartListener("123");

        // client endpoint
        var client = NullMessageQueue.Connect("123");
        Assert.NotNull(client);

        NullMessageQueue.StopListener("123");
    }

    [Fact]
    public async Task NullMessageQueue_doesnt_have_messages()
    {
        var msgs = NullMessageQueue.ReceiveMessagesAsync("123", "123", "123");
        var enumerator = msgs.GetAsyncEnumerator();
        await Assert.ThrowsAsync<TimeoutException>(async () =>
        {
            await enumerator.MoveNextAsync().AsTask().Timeout(0.5);
        });
    }

    [Fact]
    public async Task NullMessageQueue_can_have_messages()
    {
        NullMessageQueue.SendMessage("123", "123", [1, 2, 3]);

        var received = Array.Empty<byte>();
        await foreach (var msg in NullMessageQueue.ReceiveMessagesAsync(null, "123", "123"))
        {
            received = msg.Message;
        }

        Assert.Equal(3, received.Length);
    }

    [Fact]
    public async Task NullMessageQueue_can_simulate_listen_connect_and_send_operations()
    {
        // no listener yet
        var server = "server";
        Assert.Throws<Exception>(() => NullMessageQueue.Connect(server));

        // start the listener and connect successfully
        NullMessageQueue.StartListener(server);
        var client = NullMessageQueue.Connect(server);

        // send two messages to the server
        NullMessageQueue.SendMessage(client, server, [1, 2, 3], new() { { "First", "1" } });
        NullMessageQueue.SendMessage(client, server, [4, 5]);

        // receive two messages from server
        await foreach (var msg in NullMessageQueue.ReceiveMessagesAsync(null, client, server))
        {
            var expected = msg.Metadata?.Count > 0 ? "123" : "45";
            Assert.Equal(expected, string.Concat(msg.Message));
        }

        // no messages left
        var msgs = NullMessageQueue.ReceiveMessagesAsync(null, client, server);
        var enumerator = msgs.GetAsyncEnumerator();
        await Assert.ThrowsAsync<TimeoutException>(async () =>
        {
            await enumerator.MoveNextAsync().AsTask().Timeout(0.5);
        });

        // stop the listener
        NullMessageQueue.StopListener("server");

        // no listener anymore
        Assert.Throws<Exception>(() => NullMessageQueue.Connect("server"));
    }

    [Fact]
    public async Task NullClientChannel_can_connect_to_NullServerChannel()
    {
        await using var client = new NullClientTransport();
        await using var server = new NullServerChannel();

        server.SetUrl("localhost", 1234);
        client.SetUrl("localhost", 1234);

        var clientMessage = Array.Empty<byte>();
        var serverMessage = Array.Empty<byte>();

        Assert.True(server.Connections.IsEmpty);
        client.ReceiveMessage += message =>
            Console.WriteLine($"Client message received: {
                string.Concat(serverMessage = message)}");

        server.StartListening();
        await client.OpenAsync();
        await client.StartListening();

        await Task.Delay(TimeSpan.FromSeconds(0.5));
        Assert.False(server.Connections.IsEmpty);

        var connection = server.Connections.First().Value;
        connection.ReceiveMessage += message =>
            Console.WriteLine($"Server message received: {
                string.Concat(clientMessage = message)}");

        await client.SendMessageAsync([1, 2, 3, 4]);
        await connection.SendMessageAsync([3, 2, 1, 0]);

        await Task.Delay(TimeSpan.FromSeconds(0.5));
        Assert.Equal("1234", string.Concat(clientMessage));
        Assert.Equal("3210", string.Concat(serverMessage));
    }

    [Fact]
    public async Task RemotingClient_can_connect_to_RemotingServer_using_NullChannel()
    {
        using var server = new RemotingServer(new ServerConfig
        {
            MessageEncryption = false,
            Channel = ServerChannel,
            NetworkPort = 1234,
        });

        using var client = new RemotingClient(new ClientConfig
        {
            ConnectionTimeout = 5,
            MessageEncryption = false,
            Channel = ClientChannel,
            ServerPort = 1234,
        });

        server.Start();
        await client.ConnectAsync();

        await client.DisconnectAsync();
        server.Stop();
    }

    // NullTransport-specific unit tests

    private sealed class TestTransport : NullTransport
    {
        public TestTransport(string thisEndpoint, string remoteEndpoint)
        {
            ThisEndpoint = thisEndpoint;
            RemoteEndpoint = remoteEndpoint;
        }
    }

    [Fact]
    public async Task Disconnected_fires_once_on_explicit_disconnect()
    {
        var endpointA = Guid.NewGuid().ToString();
        var endpointB = Guid.NewGuid().ToString();

        var transport = new TestTransport(endpointA, endpointB);

        var fireCount = 0;
        transport.Disconnected += () => Interlocked.Increment(ref fireCount);

        await transport.StartListening();
        await transport.DisconnectAsync();

        Assert.Equal(1, fireCount);

        await transport.DisposeAsync();
    }

    [Fact]
    public async Task Disconnected_fires_once_per_cycle_across_reconnects()
    {
        var endpointA = Guid.NewGuid().ToString();
        var endpointB = Guid.NewGuid().ToString();

        var transport = new TestTransport(endpointA, endpointB);

        var fireCount = 0;
        transport.Disconnected += () => Interlocked.Increment(ref fireCount);

        await transport.StartListening();

        const int cycles = 10;
        for (var i = 0; i < cycles; i++)
        {
            await transport.DisconnectAsync();
            await transport.StartListening();
        }

        Assert.Equal(cycles, fireCount);

        await transport.DisposeAsync();
    }

    [Fact]
    public async Task Disconnected_fires_once_when_peer_sends_stop_signal()
    {
        var endpointA = Guid.NewGuid().ToString();
        var endpointB = Guid.NewGuid().ToString();

        var transport = new TestTransport(endpointA, endpointB);

        var fireCount = 0;
        transport.Disconnected += () => Interlocked.Increment(ref fireCount);

        await transport.StartListening();

        // peer sends a stop signal directly, without going through our DisconnectAsync
        NullMessageQueue.SendMessage(endpointB, endpointA, null, []);

        for (var i = 0; i < 50 && Volatile.Read(ref fireCount) == 0; i++)
            await Task.Delay(20);

        Assert.Equal(1, fireCount);

        // explicit DisconnectAsync after a spontaneous disconnect must not fire again
        await transport.DisconnectAsync();

        Assert.Equal(1, fireCount);

        await transport.DisposeAsync();
    }

    [Fact]
    public override Task Rpc_after_reconnect_does_not_throw_server_disconnected() =>
        base.Rpc_after_reconnect_does_not_throw_server_disconnected();
}