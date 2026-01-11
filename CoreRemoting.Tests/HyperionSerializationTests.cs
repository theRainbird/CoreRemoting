using System;
using System.Collections.Generic;
using CoreRemoting.Serialization.Hyperion;
using Xunit;

namespace CoreRemoting.Tests
{
	public class TestComplexObject
	{
		public int Id { get; set; }
		public string Name { get; set; }
		public DateTime CreatedAt { get; set; }
		public string[] Tags { get; set; }
		public Dictionary<string, object> Metadata { get; set; }
	}

	public class HyperionSerializationTests
	{
		[Fact]
		public void HyperionSerializerAdapter_should_serialize_and_deserialize_primitive_types()
		{
			var config = new HyperionSerializerConfig
			{
				AllowUnknownTypes = true
			};
			var serializer = new HyperionSerializerAdapter(config);

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
		public void HyperionSerializerAdapter_should_serialize_and_deserialize_complex_objects()
		{
			var config = new HyperionSerializerConfig
			{
				AllowUnknownTypes = true
			};
			var serializer = new HyperionSerializerAdapter(config);

			var originalObject = new TestComplexObject
			{
				Id = 123,
				Name = "Test Object",
				CreatedAt = DateTime.UtcNow,
				Tags = new[] { "tag1", "tag2", "tag3" },
				Metadata = new Dictionary<string, object>
				{
					["key1"] = "value1",
					["key2"] = 42,
					["key3"] = true
				}
			};

			var serialized = serializer.Serialize(originalObject);
			var deserializedObject = serializer.Deserialize<TestComplexObject>(serialized);

			Assert.NotNull(deserializedObject);
			Assert.Equal(originalObject.Id, deserializedObject.Id);
			Assert.Equal(originalObject.Name, deserializedObject.Name);
			Assert.Equal(originalObject.CreatedAt, deserializedObject.CreatedAt);
			Assert.Equal(originalObject.Tags, deserializedObject.Tags);
			Assert.Equal(originalObject.Metadata["key1"], deserializedObject.Metadata["key1"]);
			Assert.Equal(originalObject.Metadata["key2"], deserializedObject.Metadata["key2"]);
			Assert.Equal(originalObject.Metadata["key3"], deserializedObject.Metadata["key3"]);
		}

		[Fact]
		public void HyperionSerializerAdapter_should_handle_circular_references()
		{
			var config = new HyperionSerializerConfig
			{
				AllowUnknownTypes = true,
				PreserveObjectReferences = true
			};
			var serializer = new HyperionSerializerAdapter(config);

			var parent = new TestNode("Parent");
			var child = new TestNode("Child");
			parent.Children.Add(child);
			child.Parent = parent;

			var serialized = serializer.Serialize(parent);
			var deserializedParent = serializer.Deserialize<TestNode>(serialized);

			Assert.Equal(parent.Name, deserializedParent.Name);
			Assert.Single(deserializedParent.Children);
			Assert.Equal(child.Name, deserializedParent.Children[0].Name);
			Assert.Same(deserializedParent, deserializedParent.Children[0].Parent);
		}

		[Fact]
		public void HyperionSerializerAdapter_should_respect_type_restrictions()
		{
			var config = new HyperionSerializerConfig
			{
				AllowUnknownTypes = false,
				AllowedTypes = new HashSet<Type> { typeof(string), typeof(int) }
			};
			var serializer = new HyperionSerializerAdapter(config);

			// Should work with allowed types
			var allowedValue = "Hello, World!";
			var serializedAllowed = serializer.Serialize(allowedValue);
			var deserializedAllowed = serializer.Deserialize<string>(serializedAllowed);
			Assert.Equal(allowedValue, deserializedAllowed);

			// Should fail with disallowed types
			var disallowedValue = new DateTime(2023, 1, 1);
			Assert.Throws<InvalidOperationException>(() => serializer.Serialize(disallowedValue));
		}

		[Fact]
		public void HyperionSerializerAdapter_should_enforce_size_limits()
		{
			var config = new HyperionSerializerConfig
			{
				AllowUnknownTypes = true,
				MaxSerializedSize = 100 // Very small limit
			};
			var serializer = new HyperionSerializerAdapter(config);

			var largeString = new string('x', 1000);
			var serialized = serializer.Serialize(largeString);

			Assert.Throws<InvalidOperationException>(() => serializer.Deserialize<string>(serialized));
		}

		[Fact]
		public void HyperionSerializerAdapter_should_support_compression()
		{
			var config = new HyperionSerializerConfig
			{
				AllowUnknownTypes = true,
				EnableCompression = true,
				CompressionLevel = System.IO.Compression.CompressionLevel.Optimal
			};
			var serializer = new HyperionSerializerAdapter(config);

			var originalValue = new string('x', 1000); // Large string that should compress well
			var serialized = serializer.Serialize(originalValue);
			var deserializedValue = serializer.Deserialize<string>(serialized);

			Assert.Equal(originalValue, deserializedValue);
			// The compressed data should be smaller than the original
			Assert.True(serialized.Length < originalValue.Length);
		}

		[Fact]
		public void HyperionSerializerAdapter_should_clone_objects()
		{
			var config = new HyperionSerializerConfig
			{
				AllowUnknownTypes = true
			};
			var serializer = new HyperionSerializerAdapter(config);

			var original = new TestNode("Original");
			var cloned = serializer.Clone(original);

			Assert.NotSame(original, cloned);
			Assert.Equal(original.Name, cloned.Name);
			Assert.Empty(cloned.Children);
		}

		[Fact]
		public void HyperionSerializerConfig_should_validate_configuration()
		{
			var config = new HyperionSerializerConfig();

			// Valid configuration should not throw
			config.Validate();

			// Invalid MaxSerializedSize should throw
			config.MaxSerializedSize = -1;
			Assert.Throws<ArgumentException>(() => config.Validate());

			// Conflicting allowed/blocked types should throw
			config.MaxSerializedSize = 100;
			config.AllowType<string>();
			config.BlockType<string>();
			Assert.Throws<ArgumentException>(() => config.Validate());
		}

		[Fact]
		public void HyperionSerializerConfig_should_clone_correctly()
		{
			var originalConfig = new HyperionSerializerConfig
			{
				AllowUnknownTypes = false,
				PreserveObjectReferences = false,
				EnableCompression = true,
				MaxSerializedSize = 50000
			};
			originalConfig.AllowType<string>();
			originalConfig.BlockType<int>();

			var clonedConfig = originalConfig.Clone();

			// Verify all properties are copied
			Assert.Equal(originalConfig.AllowUnknownTypes, clonedConfig.AllowUnknownTypes);
			Assert.Equal(originalConfig.PreserveObjectReferences, clonedConfig.PreserveObjectReferences);
			Assert.Equal(originalConfig.EnableCompression, clonedConfig.EnableCompression);
			Assert.Equal(originalConfig.MaxSerializedSize, clonedConfig.MaxSerializedSize);
			Assert.Equal(originalConfig.AllowedTypes, clonedConfig.AllowedTypes);
			Assert.Equal(originalConfig.BlockedTypes, clonedConfig.BlockedTypes);

			// Verify collections are independent
			clonedConfig.AllowType<DateTime>();
			Assert.DoesNotContain(typeof(DateTime), originalConfig.AllowedTypes);
			Assert.Contains(typeof(DateTime), clonedConfig.AllowedTypes);
		}

		[Fact]
		public void HyperionSerializerAdapter_should_work_with_streams()
		{
			var config = new HyperionSerializerConfig
			{
				AllowUnknownTypes = true
			};
			var serializer = new HyperionSerializerAdapter(config);

			var originalValue = "Stream test value";
			
			using var memoryStream = new System.IO.MemoryStream();
			serializer.SerializeToStream(originalValue, memoryStream);
			
			memoryStream.Position = 0;
			var deserializedValue = serializer.DeserializeFromStream<string>(memoryStream);

			Assert.Equal(originalValue, deserializedValue);
		}

		// Helper class for testing circular references
		private class TestNode
		{
			public string Name { get; set; }
			public TestNode Parent { get; set; }
			public List<TestNode> Children { get; set; }

			public TestNode(string name)
			{
				Name = name;
				Children = new List<TestNode>();
			}
		}
	}
}