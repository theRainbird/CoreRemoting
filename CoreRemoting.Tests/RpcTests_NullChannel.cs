using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using CoreRemoting.Channels;
using CoreRemoting.Channels.Null;
using CoreRemoting.Tests.Tools;
using CoreRemoting.Toolbox;
using WatsonTcp;
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
        NullMessageQueue.SendMessage(client, server, [1, 2, 3], "First");
        NullMessageQueue.SendMessage(client, server, [4, 5]);

        // receive two messages from server
        await foreach (var msg in NullMessageQueue.ReceiveMessagesAsync(null, client, server))
        {
            var expected = msg.Metadata.Any() ? "1, 2, 3" : "4, 5";
            Assert.Equal(expected, string.Join(", ", msg.Message));
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
        await using var client = new NullClientChannel();
        await using var server = new NullServerChannel();

        server.SetUrl("localhost", 1234);
        client.SetUrl("localhost", 1234);

        var clientMessage = Array.Empty<byte>();
        var serverMessage = Array.Empty<byte>();

        Assert.True(server.Connections.IsEmpty);
        client.ReceiveMessage += message =>
            Console.WriteLine($"Client message received: {string.Join("", serverMessage = message)}");

        server.StartListening();
        await client.ConnectAsync();

        await Task.Delay(TimeSpan.FromSeconds(0.5));
        Assert.False(server.Connections.IsEmpty);

        var connection = server.Connections.First().Value;
        connection.ReceiveMessage += message =>
            Console.WriteLine($"Server message received: {string.Join("", clientMessage = message)}");

        await client.SendMessageAsync([1, 2, 3, 4]);
        await connection.SendMessageAsync([3, 2, 1, 0]);

        await Task.Delay(TimeSpan.FromSeconds(0.5));
        Assert.Equal("1, 2, 3, 4", string.Join(", ", clientMessage));
        Assert.Equal("3, 2, 1, 0", string.Join(", ", serverMessage));
    }

    // these tests are known to be broken
    public override void Call_on_Proxy_should_be_invoked_on_remote_service_with_MessageEncryption() =>
        Assert.Fail();

    public override Task Disposed_client_subscription_doesnt_break_other_clients() =>
        Task.Run(() => Assert.Fail());

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
}