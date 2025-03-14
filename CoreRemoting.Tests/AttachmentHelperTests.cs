using CoreRemoting.Toolbox;
using Xunit;

namespace CoreRemoting.Tests;

public class AttachmentHelperTests
{
    private object self = new();

    [Fact]
    public void AttachmentHelper_doesnt_return_non_existent_value()
    {
        Assert.False(self.Get(out int x));
    }

    [Fact]
    public void AttachmentHelper_returns_existing_string_value()
    {
        Assert.False(self.Get(out string name));
        Assert.Null(name);

        self.Set("Name");
        Assert.True(self.Get(out name));
        Assert.Equal("Name", name);
    }

    [Fact]
    public void AttachmentHelper_returns_existing_boolean_value()
    {
        Assert.False(self.Get(out bool flag));
        Assert.False(flag);

        self.Set(true);

        Assert.True(self.Get(out flag));
        Assert.True(flag);
    }
}
