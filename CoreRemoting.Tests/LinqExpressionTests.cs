using System;
using System.Linq.Expressions;
using System.Reflection;
using CoreRemoting.Tests.Tools;
using CoreRemoting.Toolbox;
using Serialize.Linq.Extensions;
using Xunit;
using System.Threading.Tasks;

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
    public void IsLinqExpressionType_method_detects_expression_types()
    {
        Assert.False(typeof(string).IsLinqExpressionType());
        Assert.False(GetType().IsLinqExpressionType());

        Expression<Func<int, bool>> predicate = i => i > 10;
        Assert.True(predicate.GetType().IsLinqExpressionType());
        Assert.True(typeof(Expression<Func<int, string>>).IsLinqExpressionType());
    }

    [Fact]
    public void LinqExpression_can_be_serialized_and_deserialized()
    {
        Expression<Func<int, string>> stringify = i => i.ToString();

        // expression can be serialized and deserialized
        var node = stringify.ToExpressionNode();
        var expr = node.ToExpression();

        Assert.Equal("i => i.ToString()", node.ToString());
        Assert.Equal("i => i.ToString()", expr.ToString());

        // deserialized expression is valid and can be compiled
        var func = (expr as Expression<Func<int, string>>).Compile();
        var result = func(123321);
        Assert.Equal("123321", result);
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

    [Fact]
    public void LinqExpression_can_be_returned_from_server_method()
    {
        using var client = new RemotingClient(new ClientConfig()
        {
            ConnectionTimeout = 0,
            MessageEncryption = false,
            ServerPort = _serverFixture.Server.Config.NetworkPort
        });

        client.Connect();
        var proxy = client.CreateProxy<IHobbitService>();

        var result = proxy.ValidatePredicate<Hobbit>(h => h.LastName.EndsWith("s"));
        var func = result.Compile();

        Assert.True(func(new Hobbit { LastName = "Baggins" }));
        Assert.False(func(new Hobbit { LastName = "Gamgee" }));
    }

    [Fact]
    public async Task LinqExpression_can_be_returned_from_async_server_method()
    {
        using var client = new RemotingClient(new ClientConfig()
        {
            ConnectionTimeout = 0,
            MessageEncryption = false,
            ServerPort = _serverFixture.Server.Config.NetworkPort
        });

        client.Connect();
        var proxy = client.CreateProxy<IHobbitService>();

        var result = await proxy.ValidatePredicateAsync<Hobbit>(h => h.LastName.EndsWith("s"));
        var func = result.Compile();

        Assert.True(func(new Hobbit { LastName = "Baggins" }));
        Assert.False(func(new Hobbit { LastName = "Gamgee" }));
    }
}