using System.Linq;
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
}
