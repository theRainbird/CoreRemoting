using System;
using System.Linq.Expressions;
using CoreRemoting.Serialization.NeoBinary;
using Xunit;

namespace CoreRemoting.Tests
{
    public class NeoBinaryExpressionSerializationTests
    {
        [Fact]
        public void SerializeDeserialize_ConstantExpression()
        {
            var serializer = new NeoBinarySerializerAdapter(new NeoBinarySerializerConfig { AllowExpressions = true, IncludeAssemblyVersions = true });

            var original = Expression.Constant(42);
            var serialized = serializer.Serialize(original);
            var deserialized = (ConstantExpression)serializer.Deserialize(typeof(ConstantExpression), serialized);

            Assert.Equal(original.Value, deserialized.Value);
            Assert.Equal(original.Type, deserialized.Type);
        }

        [Fact]
        public void SerializeDeserialize_ParameterExpression()
        {
            var serializer = new NeoBinarySerializerAdapter(new NeoBinarySerializerConfig { AllowExpressions = true, IncludeAssemblyVersions = true });

            var original = Expression.Parameter(typeof(int), "x");
            var serialized = serializer.Serialize(original);
            var deserialized = (ParameterExpression)serializer.Deserialize(typeof(ParameterExpression), serialized);

            Assert.Equal(original.Name, deserialized.Name);
            Assert.Equal(original.Type, deserialized.Type);
        }

        [Fact]
        public void SerializeDeserialize_BinaryExpression()
        {
            var serializer = new NeoBinarySerializerAdapter(new NeoBinarySerializerConfig { AllowExpressions = true, IncludeAssemblyVersions = true });

            var left = Expression.Constant(10);
            var right = Expression.Constant(20);
            var original = Expression.Add(left, right);

            var serialized = serializer.Serialize(original);
            var deserialized = (BinaryExpression)serializer.Deserialize(typeof(BinaryExpression), serialized);

            Assert.Equal(original.NodeType, deserialized.NodeType);
            Assert.Equal(((ConstantExpression)original.Left).Value, ((ConstantExpression)deserialized.Left).Value);
            Assert.Equal(((ConstantExpression)original.Right).Value, ((ConstantExpression)deserialized.Right).Value);
        }

        [Fact]
        public void SerializeDeserialize_LambdaExpression()
        {
            var serializer = new NeoBinarySerializerAdapter(new NeoBinarySerializerConfig { AllowExpressions = true, IncludeAssemblyVersions = true });

            var param = Expression.Parameter(typeof(int), "x");
            var body = Expression.Add(param, Expression.Constant(1));
            var original = Expression.Lambda<Func<int, int>>(body, param);

            var serialized = serializer.Serialize(original);
            var deserialized = (Expression<Func<int, int>>)serializer.Deserialize(typeof(Expression<Func<int, int>>), serialized);

            var compiled = deserialized.Compile();
            Assert.Equal(43, compiled(42));
        }

        [Fact]
        public void SerializeDeserialize_FilterExpression()
        {
            var serializer = new NeoBinarySerializerAdapter(new NeoBinarySerializerConfig { AllowExpressions = true, IncludeAssemblyVersions = true });

            var param = Expression.Parameter(typeof(TestClass), "item");
            var property = Expression.Property(param, nameof(TestClass.Value));
            var constant = Expression.Constant(5);
            var body = Expression.GreaterThan(property, constant);
            var original = Expression.Lambda<Func<TestClass, bool>>(body, param);

            var serialized = serializer.Serialize(original);
            var deserialized = (Expression<Func<TestClass, bool>>)serializer.Deserialize(typeof(Expression<Func<TestClass, bool>>), serialized);

            var compiled = deserialized.Compile();
            Assert.True(compiled(new TestClass { Value = 10 }));
            Assert.False(compiled(new TestClass { Value = 3 }));
        }



        public class TestClass
        {
            public int Value { get; set; }
        }
    }
}