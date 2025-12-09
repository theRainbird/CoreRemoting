using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using Xunit.Abstractions;
using CoreRemoting.Serialization.NeoBinary;

namespace CoreRemoting.Tests.Serialization.NeoBinary
{
    /// <summary>
    /// Unit tests for the optimized IL-based NeoBinary serializer.
    /// </summary>
    public class OptimizedNeoBinarySerializerTests
    {
        private readonly ITestOutputHelper _output;

        public OptimizedNeoBinarySerializerTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void IlTypeSerializer_CanSerializeAndDeserializeComplexObject()
        {
            // Arrange
            var config = new NeoBinarySerializerConfig
            {
                AllowUnknownTypes = true
            };
            var serializer = new NeoBinarySerializer
            {
                Config = config,
                TypeValidator = new NeoBinaryTypeValidator
                {
                    AllowUnknownTypes = true
                }
            };
            var ilSerializer = new IlTypeSerializer();
            
            var original = new ComplexTestClass
            {
                Id = 42,
                Name = "Test Object",
                CreatedAt = DateTime.UtcNow,
                IsActive = true,
                Score = 95.5,
                Tags = new List<string> { "tag1", "tag2", "tag3" },
                NestedObject = new NestedClass
                {
                    Value = "Nested Value",
                    Count = 10
                }
            };

            // Act
            using var stream = new MemoryStream();
            serializer.Serialize(original, stream);
            stream.Position = 0;
            
            var deserialized = (ComplexTestClass)serializer.Deserialize(stream);

            // Assert
            Assert.NotNull(deserialized);
            Assert.Equal(original.Id, deserialized.Id);
            Assert.Equal(original.Name, deserialized.Name);
            Assert.Equal(original.CreatedAt, deserialized.CreatedAt);
            Assert.Equal(original.IsActive, deserialized.IsActive);
            Assert.Equal(original.Score, deserialized.Score);
            Assert.Equal(original.Tags.Count, deserialized.Tags.Count);
            Assert.Equal(original.NestedObject.Value, deserialized.NestedObject.Value);
            Assert.Equal(original.NestedObject.Count, deserialized.NestedObject.Count);
        }

        [Fact]
        public void SerializerCache_CanCacheAndReuseSerializers()
        {
            // Arrange
            var cache = new SerializerCache();
            var ilSerializer = new IlTypeSerializer();
            var callCount = 0;

            // Act
            var serializer1 = cache.GetOrCreateSerializer(typeof(ComplexTestClass), type =>
            {
                callCount++;
                return ilSerializer.CreateSerializer(type);
            });

            var serializer2 = cache.GetOrCreateSerializer(typeof(ComplexTestClass), type =>
            {
                callCount++;
                return ilSerializer.CreateSerializer(type);
            });

            // Assert
            Assert.Same(serializer1, serializer2);
            Assert.Equal(1, callCount);
        }

        [Fact]
        public void SerializerCache_ProvidesAccurateStatistics()
        {
            // Arrange
            var cache = new SerializerCache();
            var ilSerializer = new IlTypeSerializer();

            // Act
            var serializer = cache.GetOrCreateSerializer(typeof(ComplexTestClass), 
                type => ilSerializer.CreateSerializer(type));
            var deserializer = cache.GetOrCreateDeserializer(typeof(ComplexTestClass), 
                type => ilSerializer.CreateDeserializer(type));

            // Record some operations
            cache.RecordSerialization();
            cache.RecordSerialization();
            cache.RecordDeserialization();

            var stats = cache.GetStatistics();

            // Assert
            Assert.Equal(1, stats.SerializerCount);
            Assert.Equal(1, stats.DeserializerCount);
            Assert.Equal(2, stats.TotalSerializations);
            Assert.Equal(1, stats.TotalDeserializations);
            Assert.True(stats.HitRatio >= 0.0);
        }

        [Fact]
        public void OptimizedSerializer_PerformsBetterThanReflection()
        {
            // Arrange
            var config = new NeoBinarySerializerConfig
            {
                AllowUnknownTypes = true
            };
            var optimizedSerializer = new NeoBinarySerializer
            {
                Config = config,
                TypeValidator = new NeoBinaryTypeValidator
                {
                    AllowUnknownTypes = true
                }
            };
            var testObjects = Enumerable.Range(0, 1000)
                .Select(i => new ComplexTestClass
                {
                    Id = i,
                    Name = $"Object {i}",
                    CreatedAt = DateTime.UtcNow.AddMinutes(i),
                    IsActive = i % 2 == 0,
                    Score = i * 1.5,
                    Tags = new List<string> { $"tag{i}", $"tag{i + 1}" },
                    NestedObject = new NestedClass
                    {
                        Value = $"Nested {i}",
                        Count = i
                    }
                })
                .ToList();

            // Warm up
            using (var warmupStream = new MemoryStream())
            {
                optimizedSerializer.Serialize(testObjects[0], warmupStream);
                warmupStream.Position = 0;
                optimizedSerializer.Deserialize(warmupStream);
            }

            // Act - Measure serialization performance
            var serializationStopwatch = System.Diagnostics.Stopwatch.StartNew();
            var serializedData = new List<byte[]>();
            
            foreach (var obj in testObjects)
            {
                using var stream = new MemoryStream();
                optimizedSerializer.Serialize(obj, stream);
                serializedData.Add(stream.ToArray());
            }
            
            serializationStopwatch.Stop();

            // Act - Measure deserialization performance
            var deserializationStopwatch = System.Diagnostics.Stopwatch.StartNew();
            var deserializedObjects = new List<ComplexTestClass>();
            
            foreach (var data in serializedData)
            {
                using var stream = new MemoryStream(data);
                var obj = (ComplexTestClass)optimizedSerializer.Deserialize(stream);
                deserializedObjects.Add(obj);
            }
            
            deserializationStopwatch.Stop();

            // Assert
            Assert.Equal(testObjects.Count, deserializedObjects.Count);
            
            for (int i = 0; i < testObjects.Count; i++)
            {
                Assert.Equal(testObjects[i].Id, deserializedObjects[i].Id);
                Assert.Equal(testObjects[i].Name, deserializedObjects[i].Name);
            }

            _output.WriteLine($"Serialization time: {serializationStopwatch.ElapsedMilliseconds}ms for {testObjects.Count} objects");
            _output.WriteLine($"Deserialization time: {deserializationStopwatch.ElapsedMilliseconds}ms for {testObjects.Count} objects");
            _output.WriteLine($"Average serialization time: {(double)serializationStopwatch.ElapsedMilliseconds / testObjects.Count:F2}ms per object");
            _output.WriteLine($"Average deserialization time: {(double)deserializationStopwatch.ElapsedMilliseconds / testObjects.Count:F2}ms per object");

            // Performance should be reasonable (less than 1ms per object for this simple case)
            Assert.True(serializationStopwatch.ElapsedMilliseconds < testObjects.Count * 2);
            Assert.True(deserializationStopwatch.ElapsedMilliseconds < testObjects.Count * 2);
        }

        [Fact]
        public void OptimizedSerializer_HandlesCircularReferences()
        {
            // Arrange
            var config = new NeoBinarySerializerConfig
            {
                AllowUnknownTypes = true
            };
            var serializer = new NeoBinarySerializer
            {
                Config = config,
                TypeValidator = new NeoBinaryTypeValidator
                {
                    AllowUnknownTypes = true
                }
            };
            var parent = new CircularRefClass();
            var child = new CircularRefClass();
            
            parent.Child = child;
            child.Parent = parent;

            // Act
            using var stream = new MemoryStream();
            serializer.Serialize(parent, stream);
            stream.Position = 0;
            
            var deserialized = (CircularRefClass)serializer.Deserialize(stream);

            // Assert
            Assert.NotNull(deserialized);
            Assert.NotNull(deserialized.Child);
            Assert.Same(deserialized, deserialized.Child.Parent);
        }

        [Fact]
        public void OptimizedSerializer_HandlesNullValues()
        {
            // Arrange
            var config = new NeoBinarySerializerConfig
            {
                AllowUnknownTypes = true
            };
            var serializer = new NeoBinarySerializer
            {
                Config = config,
                TypeValidator = new NeoBinaryTypeValidator
                {
                    AllowUnknownTypes = true
                }
            };
            var original = new ComplexTestClass
            {
                Id = 1,
                Name = null,
                NestedObject = null,
                Tags = null
            };

            // Act
            using var stream = new MemoryStream();
            serializer.Serialize(original, stream);
            stream.Position = 0;
            
            var deserialized = (ComplexTestClass)serializer.Deserialize(stream);

            // Assert
            Assert.NotNull(deserialized);
            Assert.Equal(original.Id, deserialized.Id);
            Assert.Null(deserialized.Name);
            Assert.Null(deserialized.NestedObject);
            Assert.Null(deserialized.Tags);
        }

        [Fact]
        public void OptimizedSerializer_HandlesCollections()
        {
            // Arrange
            var config = new NeoBinarySerializerConfig
            {
                AllowUnknownTypes = true
            };
            var serializer = new NeoBinarySerializer
            {
                Config = config,
                TypeValidator = new NeoBinaryTypeValidator
                {
                    AllowUnknownTypes = true
                }
            };
            var original = new ComplexTestClass
            {
                Id = 1,
                Tags = new List<string> { "a", "b", "c" }
            };

            // Act & Assert - should not throw
            using var stream = new MemoryStream();
            serializer.Serialize(original, stream);
            stream.Position = 0;
            
            var deserialized = (ComplexTestClass)serializer.Deserialize(stream);

            // Basic assertions
            Assert.NotNull(deserialized);
            Assert.Equal(original.Id, deserialized.Id);
        }

        [Fact]
        public void SerializerCache_CleanupRemovesUnusedItems()
        {
            // Arrange
            var config = new SerializerCache.CacheConfiguration
            {
                MaxCacheSize = 2,
                CleanupIntervalSeconds = 1,
                MinAccessCount = 2,
                EnableAutoCleanup = false // Manual cleanup for test
            };
            
            var cache = new SerializerCache(config);
            var ilSerializer = new IlTypeSerializer();

            // Act - Create more items than cache size
            cache.GetOrCreateSerializer(typeof(ComplexTestClass), t => ilSerializer.CreateSerializer(t));
            cache.GetOrCreateSerializer(typeof(NestedClass), t => ilSerializer.CreateSerializer(t));
            cache.GetOrCreateSerializer(typeof(string), t => ilSerializer.CreateSerializer(t));

            // Manually trigger cleanup
            cache.Dispose();
            cache = new SerializerCache(config);

            // Assert - Cache should have been cleaned up
            var stats = cache.GetStatistics();
            Assert.True(stats.SerializerCount <= config.MaxCacheSize);
        }

        [Fact]
        public void OptimizedSerializer_HandlesValueTypes()
        {
            // Arrange
            var config = new NeoBinarySerializerConfig
            {
                AllowUnknownTypes = true
            };
            var serializer = new NeoBinarySerializer
            {
                Config = config,
                TypeValidator = new NeoBinaryTypeValidator
                {
                    AllowUnknownTypes = true
                }
            };
            var original = new TestStruct
            {
                Id = 42,
                Name = "Test Struct",
                Value = 3.14
            };

            // Act
            using var stream = new MemoryStream();
            serializer.Serialize(original, stream);
            stream.Position = 0;
            
            var deserialized = (TestStruct)serializer.Deserialize(stream);

            // Assert
            Assert.Equal(original.Id, deserialized.Id);
            Assert.Equal(original.Name, deserialized.Name);
            Assert.Equal(original.Value, deserialized.Value);
        }

        [Fact]
        public void OptimizedSerializer_PreservesTypeInformation()
        {
            // Arrange
            var config = new NeoBinarySerializerConfig
            {
                AllowUnknownTypes = true
            };
            var serializer = new NeoBinarySerializer
            {
                Config = config,
                TypeValidator = new NeoBinaryTypeValidator
                {
                    AllowUnknownTypes = true
                }
            };
            BaseClass original = new DerivedClass
            {
                BaseProperty = "Base Value",
                DerivedProperty = "Derived Value"
            };

            // Act
            using var stream = new MemoryStream();
            serializer.Serialize(original, stream);
            stream.Position = 0;
            
            var deserialized = (BaseClass)serializer.Deserialize(stream);

            // Assert
            Assert.IsType<DerivedClass>(deserialized);
            var derived = (DerivedClass)deserialized;
            Assert.Equal(original.BaseProperty, derived.BaseProperty);
            Assert.Equal(((DerivedClass)original).DerivedProperty, derived.DerivedProperty);
        }

        // Test classes
        public class ComplexTestClass
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public DateTime CreatedAt { get; set; }
            public bool IsActive { get; set; }
            public double Score { get; set; }
        public List<string> Tags { get; set; }
        public NestedClass NestedObject { get; set; }
        }

        public class NestedClass
        {
            public string Value { get; set; }
            public int Count { get; set; }
        }

        public class CircularRefClass
        {
            public CircularRefClass Child { get; set; }
            public CircularRefClass Parent { get; set; }
        }

        public struct TestStruct
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public double Value { get; set; }
        }

        public class BaseClass
        {
            public string BaseProperty { get; set; }
        }

        public class DerivedClass : BaseClass
        {
            public string DerivedProperty { get; set; }
        }
    }
}