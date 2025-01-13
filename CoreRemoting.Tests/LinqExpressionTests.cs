using CoreRemoting.Tests.Tools;
using Xunit;

namespace CoreRemoting.Tests;

[Collection("CoreRemoting")]
public class LinqExpressionTests : IClassFixture<ServerFixture>
{
    private ServerFixture _serverFixture;

    public LinqExpressionTests(ServerFixture serverFixture)
    {
        _serverFixture = serverFixture;
        _serverFixture.Start();
    }
    
    [Fact]
    public void LinqExpression_should_be_serialized_and_deserialized()
    {
        using var client = new RemotingClient(new ClientConfig()
        {
            ConnectionTimeout = 0, 
            MessageEncryption = false,
            ServerPort = _serverFixture.Server.Config.NetworkPort
        });

        client.Connect();
        var proxy = client.CreateProxy<IHobbitService>();

        var result = proxy.QueryHobbits(h => h.FirstName == "Frodo");
        
        Assert.True(result.FirstName == "Frodo");
    }
}