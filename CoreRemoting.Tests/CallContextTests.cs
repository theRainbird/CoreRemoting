using System.Threading;
using CoreRemoting.Tests.Tools;
using Xunit;

namespace CoreRemoting.Tests;

[Collection("CoreRemoting")]
public class CallContextTests : IClassFixture<ServerFixture>
{
    private ServerFixture _serverFixture;
    
    public CallContextTests(ServerFixture serverFixture)
    {
        _serverFixture = serverFixture;
        _serverFixture.Start();
    }
    
    [Fact]
    public void CallContext_should_flow_from_client_to_server_and_back()
    {
        _serverFixture.TestService.TestMethodFake = _ =>
        {
            CallContext.SetData("test", "Changed");
            return CallContext.GetData("test");
        };
        
        var clientThread =
            new Thread(() =>
            {
                CallContext.SetData("test", "CallContext");

                var client =
                    new RemotingClient(new ClientConfig()
                    {
                        ServerPort = _serverFixture.Server.Config.NetworkPort,
                        MessageEncryption = false,
                        ConnectionTimeout = 0
                    });

                client.Connect();

                var localCallContextValueBeforeRpc = CallContext.GetData("test");
                
                var proxy = client.CreateProxy<ITestService>();
                var result = (string) proxy.TestMethod("x");

                var localCallContextValueAfterRpc = CallContext.GetData("test");
                
                Assert.NotEqual(localCallContextValueBeforeRpc, result);
                Assert.Equal("Changed", result);
                Assert.Equal("Changed", localCallContextValueAfterRpc);

                client.Dispose();
            });
        
        clientThread.Start();
        clientThread.Join();
    }
}