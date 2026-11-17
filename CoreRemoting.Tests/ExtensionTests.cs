using System;
using System.Linq;
using CoreRemoting.Authentication;
using CoreRemoting.Toolbox;
using Xunit;

namespace CoreRemoting.Tests;

public class ExtensionTests
{
    [Fact]
    public void AppendDoesntFailOnNulls()
    {
        string[] strings = null;
        strings = strings.Append("Hello");
        Assert.NotNull(strings);
        Assert.Single(strings);
        Assert.Equal("Hello", strings.Single());

        strings = strings.Append(values: null);
        Assert.NotNull(strings);
        Assert.Single(strings);
        Assert.Equal("Hello", strings.Single());

        strings = strings.Append("World");
        Assert.NotNull(strings);
        Assert.Equal(2, strings.Length);
        Assert.Equal("World", strings.Last());
    }

    [Fact]
    public void AppendConcatenatesArrays()
    {
        var numbers = new[] { 1, 2, 3, 4 }.Append(5, 6, 7);
        Assert.Equal(7, numbers.Length);
        Assert.Equal("1234567", string.Concat(numbers));
    }

    [Fact]
    public void AppendAnonymousDoesntFailOnNulls()
    {
        var creds = default(Credential[]);
        creds = creds.Append(null);
        Assert.NotNull(creds);
        Assert.Empty(creds);

        creds = [];
        creds = creds.Append(null);
        Assert.NotNull(creds);
        Assert.Empty(creds);
    }

    [Fact]
    public void AppendAnonymousConvertsPropertiesToCredentials()
    {
        var creds = Array.Empty<Credential>();
        creds = creds.Append(new
        {
            login = "yallie",
            password = "secret",
            token = (string)null, // should be ignored
            empty = ""
        });

        Assert.Equal(3, creds.Length);
        Assert.Contains(creds, c => c.Name == "login" && c.Value == "yallie");
        Assert.Contains(creds, c => c.Name == "password" && c.Value == "secret");
        Assert.Contains(creds, c => c.Name == "empty" && c.Value == "");
        Assert.DoesNotContain(creds, c => c.Name == "token");
    }

    [Fact]
    public void AppendAnonymousAddsToExistingArray()
    {
        var existing = new Credential[]
        {
            new() { Name = "existing", Value = "value" }
        };

        var result = existing.Append(new
        {
            foo = "bar",
            baz = "qux"
        });

        Assert.Equal(3, result.Length);
        Assert.Contains(result, c => c.Name == "existing" && c.Value == "value");
        Assert.Contains(result, c => c.Name == "foo" && c.Value == "bar");
        Assert.Contains(result, c => c.Name == "baz" && c.Value == "qux");
    }
}
