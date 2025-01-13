using System;
using System.Reflection;
using CoreRemoting.Serialization;
using Xunit;

namespace CoreRemoting.Tests;

public class ExceptionTests
{
    /// <summary>
    /// Private non-serializable exception class.
    /// </summary>
    class NonSerializableException : Exception
    {
        public NonSerializableException()
            : this("This exception is not serializable")
        {
        }

        public NonSerializableException(string message, Exception inner = null)
            : base(message, inner)
        {
        }
    }

    [Fact]
    public void Exception_can_be_checked_if_it_is_serializable()
    {
        Assert.True(new Exception().IsSerializable());
        Assert.False(new NonSerializableException().IsSerializable());
        Assert.True(new Exception("Hello", new Exception()).IsSerializable());
        Assert.False(new Exception("Goodbye", new NonSerializableException()).IsSerializable());
    }

    [Fact]
    public void Exception_can_be_turned_to_serializable()
    {
        var slotName = "SomeData";
        var ex = new Exception("Bang!", new NonSerializableException("Zoom!"));
        ex.Data[slotName] = DateTime.Now.ToString();
        ex.InnerException.Data[slotName] = DateTime.Today.Ticks;
        Assert.False(ex.IsSerializable());

        var sx = ex.ToSerializable();
        Assert.True(sx.IsSerializable());
        Assert.NotSame(ex, sx);
        Assert.NotSame(ex.InnerException, sx.InnerException);

        Assert.Equal(ex.Message, sx.Message);
        Assert.Equal(ex.Data[slotName], sx.Data[slotName]);
        Assert.Equal(ex.InnerException.Message, sx.InnerException.Message);
        Assert.Equal(ex.InnerException.Data[slotName], sx.InnerException.Data[slotName]);
    }

    [Fact]
    public void SkipTargetInvocationException_returns_the_first_meaningful_inner_exception()
    {
        // the first meaningful exception
        var ex = new Exception("Hello");
        var tex = new TargetInvocationException(ex);
        Assert.Equal(ex, tex.SkipTargetInvocationExceptions());

        // no inner exceptions, return as is
        tex = new TargetInvocationException(null);
        Assert.Equal(tex, tex.SkipTargetInvocationExceptions());

        // null, return as is
        Assert.Null(default(Exception).SkipTargetInvocationExceptions());
    }
}