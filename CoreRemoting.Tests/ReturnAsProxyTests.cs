using Xunit;
using System;
using System.Threading;
using CoreRemoting.ClassicRemotingApi;
using CoreRemoting.Tests.Tools;
using Xunit.Abstractions;

namespace CoreRemoting.Tests;

[Collection("CoreRemoting")]
public class ReturnAsProxyTests : IClassFixture<ServerFixture>
{
    private ServerFixture _serverFixture;
    private readonly ITestOutputHelper _testOutputHelper;

    public ReturnAsProxyTests(ServerFixture serverFixture, ITestOutputHelper testOutputHelper)
    {
        _serverFixture = serverFixture;
        _testOutputHelper = testOutputHelper;
        _serverFixture.Start();
    }
    
    [Fact]
    public void Call_on_Proxy_should_be_invoked_on_remote_service()
    {
        void ClientAction()
        {
            try
            {
                using var client = new RemotingClient(new ClientConfig()
                {
                    ConnectionTimeout = 0, 
                    MessageEncryption = false,
                    ServerPort = _serverFixture.Server.Config.NetworkPort
                });

                client.Connect();

                var factoryServiceProxy = client.CreateProxy<IFactoryService>();
                var testServiceProxy = factoryServiceProxy.GetTestService();

                Assert.True(RemotingServices.IsTransparentProxy(testServiceProxy));
            }
            catch (Exception e)
            {
                _testOutputHelper.WriteLine(e.ToString());
                throw;
            }
        }

        var clientThread = new Thread(ClientAction);
        clientThread.Start();
        clientThread.Join();
    }
}