using System;
using System.Collections.Generic;
using System.Linq;
using CoreRemoting.Serialization.NeoBinary;
using Xunit;

namespace CoreRemoting.Tests
{
    public class NeoBinarySerializationTests
    {
        [Fact]
        public void NeoBinarySerializerAdapter_should_serialize_and_deserialize_primitive_types()
        {
            var config = new NeoBinarySerializerConfig();
            config.AllowUnknownTypes = true;
            var serializer = new NeoBinarySerializerAdapter(config);

            // Test all primitive types
            var testValues = new object[]
            {
                true,
                (byte)42,
                (sbyte)-42,
                (short)1234,
                (ushort)1234,
                123456,
                123456U,
                123456789L,
                123456789UL,
                3.14f,
                3.14159265359,
                123.456m,
                "Hello, World!",
                null
            };

            foreach (var originalValue in testValues)
            {
                var serialized = serializer.Serialize(originalValue);
                var deserializedValue = serializer.Deserialize<object>(serialized);

                Assert.Equal(originalValue, deserializedValue);
            }
        }

        [Fact]
        public void NeoBinarySerializerAdapter_should_serialize_and_deserialize_complex_objects()
        {
            var serializer = new NeoBinarySerializerAdapter();

            var testObject = new TestComplexObject
            {
                Id = 123,
                Name = "Test Object",
                CreatedDate = DateTime.Now,
                IsActive = true,
                Tags = new List<string> { "tag1", "tag2", "tag3" },
                Metadata = new Dictionary<string, object>
                {
                    ["key1"] = "value1",
                    ["key2"] = 42,
                    ["key3"] = true
                }
            };

            var serialized = serializer.Serialize(testObject);
            var deserializedObject = serializer.Deserialize<TestComplexObject>(serialized);

            Assert.NotNull(deserializedObject);
            Assert.Equal(testObject.Id, deserializedObject.Id);
            Assert.Equal(testObject.Name, deserializedObject.Name);
            Assert.Equal(testObject.IsActive, deserializedObject.IsActive);
            Assert.Equal(testObject.Tags.Count, deserializedObject.Tags.Count);
            Assert.Equal(testObject.Metadata.Count, deserializedObject.Metadata.Count);
        }

        [Fact]
        public void NeoBinarySerializerAdapter_should_handle_circular_references()
        {
            var serializer = new NeoBinarySerializerAdapter();

            var parent = new TestNode { Name = "Parent" };
            var child = new TestNode { Name = "Child" };
            
            parent.Children.Add(child);
            child.Parent = parent;

            var serialized = serializer.Serialize(parent);
            var deserializedParent = serializer.Deserialize<TestNode>(serialized);

            Assert.NotNull(deserializedParent);
            Assert.Equal("Parent", deserializedParent.Name);
            Assert.Single(deserializedParent.Children);
            Assert.Equal("Child", deserializedParent.Children[0].Name);
            Assert.Same(deserializedParent, deserializedParent.Children[0].Parent);
        }

        [Fact]
        public void NeoBinarySerializerAdapter_should_serialize_and_deserialize_arrays()
        {
            var serializer = new NeoBinarySerializerAdapter();

            // Test single-dimensional array
            var intArray = new[] { 1, 2, 3, 4, 5 };
            var serialized = serializer.Serialize(intArray);
            var deserializedArray = serializer.Deserialize<int[]>(serialized);

            Assert.Equal(intArray.Length, deserializedArray.Length);
            for (int i = 0; i < intArray.Length; i++)
            {
                Assert.Equal(intArray[i], deserializedArray[i]);
            }

            // Test multi-dimensional array
            var multiArray = new[,] { { 1, 2 }, { 3, 4 } };
            serialized = serializer.Serialize(multiArray);
            var deserializedMultiArray = serializer.Deserialize<int[,]>(serialized);

            Assert.Equal(2, deserializedMultiArray.GetLength(0));
            Assert.Equal(2, deserializedMultiArray.GetLength(1));
            Assert.Equal(1, deserializedMultiArray[0, 0]);
            Assert.Equal(2, deserializedMultiArray[0, 1]);
            Assert.Equal(3, deserializedMultiArray[1, 0]);
            Assert.Equal(4, deserializedMultiArray[1, 1]);
        }

        [Fact]
        public void NeoBinarySerializerAdapter_should_serialize_and_deserialize_collections()
        {
            var serializer = new NeoBinarySerializerAdapter();

            // Test List<T>
            var list = new List<string> { "item1", "item2", "item3" };
            var serialized = serializer.Serialize(list);
            var deserializedList = serializer.Deserialize<List<string>>(serialized);

            Assert.Equal(list.Count, deserializedList.Count);
            for (int i = 0; i < list.Count; i++)
            {
                Assert.Equal(list[i], deserializedList[i]);
            }

            // Test Dictionary<K,V>
            var dictionary = new Dictionary<int, string>
            {
                [1] = "one",
                [2] = "two",
                [3] = "three"
            };
            serialized = serializer.Serialize(dictionary);
            var deserializedDictionary = serializer.Deserialize<Dictionary<int, string>>(serialized);

            Assert.Equal(dictionary.Count, deserializedDictionary.Count);
            foreach (var kvp in dictionary)
            {
                Assert.Equal(kvp.Value, deserializedDictionary[kvp.Key]);
            }
        }

        [Fact]
        public void NeoBinarySerializerAdapter_should_serialize_and_deserialize_enums()
        {
            var serializer = new NeoBinarySerializerAdapter();

            var testEnum = TestEnum.Option2;
            var serialized = serializer.Serialize(testEnum);
            var deserializedEnum = serializer.Deserialize<TestEnum>(serialized);

            Assert.Equal(testEnum, deserializedEnum);
        }

        [Fact]
        public void NeoBinarySerializerAdapter_should_respect_type_validation()
        {
            var config = new NeoBinarySerializerConfig();
            config.AllowType<string>();
            config.AllowType<int>();
            config.AllowUnknownTypes = false;

            var serializer = new NeoBinarySerializerAdapter(config);

            // Should work for allowed types
            var stringValue = "test";
            var serialized = serializer.Serialize(stringValue);
            var deserialized = serializer.Deserialize<string>(serialized);
            Assert.Equal(stringValue, deserialized);

            // Should fail for blocked types
            var blockedObject = new TestComplexObject();
            serialized = serializer.Serialize(blockedObject);
            
            Assert.Throws<NeoBinaryUnsafeDeserializationException>(() => 
                serializer.Deserialize<TestComplexObject>(serialized));
        }

        [Fact]
        public void NeoBinarySerializerAdapter_should_handle_null_values()
        {
            var serializer = new NeoBinarySerializerAdapter();

            TestComplexObject nullObject = null;
            var serialized = serializer.Serialize(nullObject);
            var deserialized = serializer.Deserialize<TestComplexObject>(serialized);

            Assert.Null(deserialized);
        }

        [Fact]
        public void NeoBinarySerializerAdapter_should_clone_objects()
        {
            var serializer = new NeoBinarySerializerAdapter();

            var original = new TestComplexObject
            {
                Id = 123,
                Name = "Original",
                CreatedDate = DateTime.Now,
                IsActive = true
            };

            var cloned = serializer.Clone(original);

            Assert.NotNull(cloned);
            Assert.NotSame(original, cloned);
            Assert.Equal(original.Id, cloned.Id);
            Assert.Equal(original.Name, cloned.Name);
            Assert.Equal(original.CreatedDate, cloned.CreatedDate);
            Assert.Equal(original.IsActive, cloned.IsActive);
        }

        [Fact]
        public void NeoBinarySerializerConfig_should_validate_settings()
        {
            var config = new NeoBinarySerializerConfig();

            // Valid configuration should not throw
            config.Validate();

            // Invalid MaxObjectGraphDepth should throw
            config.MaxObjectGraphDepth = 0;
            Assert.Throws<ArgumentException>(() => config.Validate());

            // Reset and test MaxSerializedSize
            config.MaxObjectGraphDepth = 100;
            config.MaxSerializedSize = 0;
            Assert.Throws<ArgumentException>(() => config.Validate());
        }

        [Fact]
        public void NeoBinaryTypeValidator_should_block_dangerous_types()
        {
            var validator = new NeoBinaryTypeValidator
            {
                AllowUnknownTypes = false,
                StrictTypeChecking = true
            };

            // Should block delegates
            Assert.False(validator.IsTypeAllowed(typeof(Func<string>)));

            // Should block types from blocked namespaces
            validator.BlockNamespace("System.Management.Automation");
            Assert.False(validator.IsTypeAllowed(typeof(object))); // object is in System namespace, not blocked
            
            // Should allow explicitly allowed types
            validator.AllowType<string>();
            Assert.True(validator.IsTypeAllowed(typeof(string)));
        }

        [Fact]
        public void NeoBinarySerializerAdapter_should_work_with_compression()
        {
            var config = new NeoBinarySerializerConfig
            {
                EnableCompression = true
            };

            var serializer = new NeoBinarySerializerAdapter(config);

            var testObject = new TestComplexObject
            {
                Id = 123,
                Name = "Test with compression",
                Tags = new List<string>(Enumerable.Repeat("large tag content", 100))
            };

            var serialized = serializer.Serialize(testObject);
            var deserialized = serializer.Deserialize<TestComplexObject>(serialized);

            Assert.NotNull(deserialized);
            Assert.Equal(testObject.Id, deserialized.Id);
            Assert.Equal(testObject.Name, deserialized.Name);
            Assert.Equal(testObject.Tags.Count, deserialized.Tags.Count);
        }

        [Fact]
        public void NeoBinarySerializerAdapter_should_serialize_and_deserialize_exceptions()
        {
            var serializer = new NeoBinarySerializerAdapter();

            // Test basic Exception
            var basicException = new Exception("Test exception message");
            var serialized = serializer.Serialize(basicException);
            var deserializedException = serializer.Deserialize<Exception>(serialized);

            Assert.NotNull(deserializedException);
            Assert.Equal(basicException.Message, deserializedException.Message);
            Assert.Equal(basicException.HResult, deserializedException.HResult);
            Assert.Null(deserializedException.InnerException);

            // Test Exception with inner exception
            var innerException = new InvalidOperationException("Inner exception");
            var outerException = new Exception("Outer exception", innerException);
            serialized = serializer.Serialize(outerException);
            deserializedException = serializer.Deserialize<Exception>(serialized);

            Assert.NotNull(deserializedException);
            Assert.Equal(outerException.Message, deserializedException.Message);
            Assert.NotNull(deserializedException.InnerException);
            Assert.Equal(innerException.Message, deserializedException.InnerException.Message);

            // Test ArgumentException with parameters
            var argumentException = new ArgumentException("Invalid argument", "parameterName");
            serialized = serializer.Serialize(argumentException);
            deserializedException = serializer.Deserialize<ArgumentException>(serialized);

            Assert.NotNull(deserializedException);
            Assert.IsType<ArgumentException>(deserializedException);
            var argEx = (ArgumentException)deserializedException;
            Assert.Equal(argumentException.Message, argEx.Message);
            Assert.Equal(argumentException.ParamName, argEx.ParamName);

            // Test custom exception
            var customException = new TestCustomException(message: "Custom exception message")
            {
                ErrorCode = 123,
                AdditionalInfo = "Additional data"
            };
            serialized = serializer.Serialize(customException);
            deserializedException = serializer.Deserialize<TestCustomException>(serialized);

            Assert.NotNull(deserializedException);
            Assert.IsType<TestCustomException>(deserializedException);
            var customEx = (TestCustomException)deserializedException;
            Assert.Equal(customException.Message, customEx.Message);
            Assert.Equal(customException.ErrorCode, customEx.ErrorCode);
            Assert.Equal(customException.AdditionalInfo, customEx.AdditionalInfo);
        }

        [Fact]
        public void NeoBinarySerializerAdapter_should_serialize_exception_with_data()
        {
            var serializer = new NeoBinarySerializerAdapter();

            var exception = new Exception("Test exception");
            exception.Data["key1"] = "value1";
            exception.Data["key2"] = 42;
            exception.Data["key3"] = true;

            var serialized = serializer.Serialize(exception);
            var deserializedException = serializer.Deserialize<Exception>(serialized);

            Assert.NotNull(deserializedException);
            Assert.Equal(exception.Message, deserializedException.Message);
            Assert.Equal(exception.Data.Count, deserializedException.Data.Count);
            Assert.Equal("value1", deserializedException.Data["key1"]);
            Assert.Equal(42, deserializedException.Data["key2"]);
            Assert.Equal(true, deserializedException.Data["key3"]);
        }

        [Fact]
        public void NeoBinarySerializerAdapter_should_serialize_exception_with_stack_trace()
        {
            var serializer = new NeoBinarySerializerAdapter();

            Exception exception;
            try
            {
                throw new Exception("Test exception with stack trace");
            }
            catch (Exception ex)
            {
                exception = ex;
            }

            var serialized = serializer.Serialize(exception);
            var deserializedException = serializer.Deserialize<Exception>(serialized);

            Assert.NotNull(deserializedException);
            Assert.Equal(exception.Message, deserializedException.Message);
            Assert.NotNull(deserializedException.StackTrace);
            Assert.Contains("Test exception with stack trace", deserializedException.StackTrace);
        }

        [Serializable]
        public class TestComplexObject
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public DateTime CreatedDate { get; set; }
            public bool IsActive { get; set; }
            public List<string> Tags { get; set; } = new List<string>();
            public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
        }

        [Serializable]
        public class TestNode
        {
            public string Name { get; set; }
            public TestNode Parent { get; set; }
            public List<TestNode> Children { get; set; } = new List<TestNode>();
        }

        public enum TestEnum
        {
            Option1,
            Option2,
            Option3
        }

        [Serializable]
        public class TestCustomException : Exception
        {
            public int ErrorCode { get; set; }
            public string AdditionalInfo { get; set; }

            public TestCustomException() : base()
            {
            }

            public TestCustomException(string message) : base(message)
            {
            }

            public TestCustomException(string message, Exception innerException) : base(message, innerException)
            {
            }
        }
    }
}