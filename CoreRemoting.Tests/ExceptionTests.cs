using System;
using System.Reflection;
using CoreRemoting.Serialization;
using Xunit;

namespace CoreRemoting.Tests
{
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
}