using System;
using System.Linq;
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
        NullMessageQueue.SendMessage("123", "123", "123", [1, 2, 3]);

        var received = Array.Empty<byte>();
        await foreach (var msg in NullMessageQueue.ReceiveMessagesAsync("123", "123", "123"))
        {
            received = msg.Message;
        }

        Assert.Equal(3, received.Length);
    }

    [Fact]
    public async Task NullClientChannel_can_connect_to_NullServerChannel()
    {
        await using var client = new NullClientChannel();
        await using var server = new NullServerChannel();

        server.SetUrl("localhost", 1234);
        client.SetUrl("localhost", 1234);

        Assert.True(server.Connections.IsEmpty);
        client.ReceiveMessage += message =>
            Console.WriteLine($"Client message received: {string.Join("", message)}");

        server.StartListening();
        await client.ConnectAsync();

        await Task.Delay(TimeSpan.FromSeconds(0.5));
        Assert.False(server.Connections.IsEmpty);

        var connection = server.Connections.First().Value;
        connection.ReceiveMessage += message =>
            Console.WriteLine($"Server message received: {string.Join("", message)}");

        await client.SendMessageAsync([1, 2, 3, 4]);
        await connection.SendMessageAsync([3, 2, 1, 0]);
        await Task.Delay(TimeSpan.FromSeconds(0.5));
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
}