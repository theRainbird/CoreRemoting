using System;
using System.Collections.Generic;
using CoreRemoting.Serialization.NeoBinary;
using System.Runtime.Serialization;
using Xunit;

public class NeoBinaryCustomSerializationTests
{
	[Fact]
	public void CustomSerialization_should_serialize_and_deserialize_primitive_properties()
	{
		var serializer = new NeoBinarySerializerAdapter();

		var original = new SimpleCustomObject(42, "Test Name");

		var serialized = serializer.Serialize(original);
		var deserialized = serializer.Deserialize<SimpleCustomObject>(serialized);

		Assert.Equal(original.Id, deserialized.Id);
		Assert.Equal(original.Name, deserialized.Name);
	}

	[Fact]
	public void CustomSerialization_should_serialize_and_deserialize_nested_objects()
	{
		var serializer = new NeoBinarySerializerAdapter();

		var original = new NestedCustomObject
		{
			Child = new SimpleCustomObject(123, "Nested")
		};

		var serialized = serializer.Serialize(original);
		var deserialized = serializer.Deserialize<NestedCustomObject>(serialized);

		Assert.NotNull(deserialized.Child);
		Assert.Equal(123, deserialized.Child.Id);
		Assert.Equal("Nested", deserialized.Child.Name);
	}

	[Fact]
	public void CustomSerialization_should_serialize_and_deserialize_with_null_values()
	{
		var serializer = new NeoBinarySerializerAdapter();

		var original = new CustomObjectWithNulls(42, null);

		var serialized = serializer.Serialize(original);
		var deserialized = serializer.Deserialize<CustomObjectWithNulls>(serialized);

		Assert.Equal(42, deserialized.Id);
		Assert.Null(deserialized.Name);
	}

	[Fact(Skip = "Circular references are not supported for ICustomSerialization objects")]
	public void CustomSerialization_should_handle_circular_references()
	{
		var serializer = new NeoBinarySerializerAdapter();

		var parent = new NodeWithCustomSerialization("Parent");
		var child = new NodeWithCustomSerialization("Child");

		parent.Child = child;
		child.Parent = parent;

		var serialized = serializer.Serialize(parent);
		var deserializedParent = serializer.Deserialize<NodeWithCustomSerialization>(serialized);

		Assert.Equal("Parent", deserializedParent.Name);
		Assert.NotNull(deserializedParent.Child);
		Assert.Equal("Child", deserializedParent.Child.Name);
		Assert.Same(deserializedParent, deserializedParent.Child.Parent);
	}

	[Fact]
	public void CustomSerialization_respect_config_enable_false()
	{
		var config = new NeoBinarySerializerConfig
		{
			EnableCustomSerialization = false
		};
		var serializer = new NeoBinarySerializerAdapter(config);

		var original = new SimpleCustomObject(42, "Test");

		var serialized = serializer.Serialize(original);
		var deserialized = serializer.Deserialize<SimpleCustomObject>(serialized);

		Assert.Equal(42, deserialized.Id);
		Assert.Equal("Test", deserialized.Name);
	}

	[Fact]
	public void CustomSerialization_should_throw_if_constructor_missing()
	{
		var serializer = new NeoBinarySerializerAdapter();

		var original = new CustomObjectWithoutConstructor();

		var ex = Assert.Throws<InvalidOperationException>(() =>
		{
			var serialized = serializer.Serialize(original);
			serializer.Deserialize<CustomObjectWithoutConstructor>(serialized);
		});
		Assert.Contains("constructor", ex.InnerException?.Message);
	}

	[Fact]
	public void CustomSerialization_should_work_with_collections()
	{
		var serializer = new NeoBinarySerializerAdapter();

		var list = new List<SimpleCustomObject>
		{
			new(1, "First"),
			new(2, "Second"),
			new(3, "Third")
		};

		var serialized = serializer.Serialize(list);
		var deserialized = serializer.Deserialize<List<SimpleCustomObject>>(serialized);

		Assert.Equal(3, deserialized.Count);
		Assert.Equal(1, deserialized[0].Id);
		Assert.Equal(2, deserialized[1].Id);
		Assert.Equal(3, deserialized[2].Id);
	}

	[Fact]
	public void CustomSerialization_should_serialize_complex_types_in_data()
	{
		var serializer = new NeoBinarySerializerAdapter();

		var original = new CustomObjectWithComplexTypes
		{
			Id = 1,
			Values = [10, 20, 30],
			Metadata = new Dictionary<string, object>
			{
				["key1"] = "value1",
				["key2"] = 42
			}
		};

		var serialized = serializer.Serialize(original);
		var deserialized = serializer.Deserialize<CustomObjectWithComplexTypes>(serialized);

		Assert.Equal(1, deserialized.Id);
		Assert.Equal(3, deserialized.Values.Count);
		Assert.Equal(2, deserialized.Metadata.Count);
		Assert.Equal("value1", deserialized.Metadata["key1"]);
		Assert.Equal(42, deserialized.Metadata["key2"]);
	}

	[Serializable]
	public class SimpleCustomObject : ICustomSerialization
	{
		public int Id { get; private set; }
		public string Name { get; private set; }

		public SimpleCustomObject() { }

		public SimpleCustomObject(int id, string name)
		{
			Id = id;
			Name = name;
		}

		protected SimpleCustomObject(List<CustomSerializationData> data)
		{
			foreach (var entry in data)
			{
				if (entry.Name == "Id") Id = (int)entry.Value;
				else if (entry.Name == "Name") Name = (string)entry.Value;
			}
		}

		public List<CustomSerializationData> GetSerializationData()
			=> new()
			{
				new() { Name = "Id", Type = typeof(int), Value = Id },
				new() { Name = "Name", Type = typeof(string), Value = Name }
			};
	}

	[Serializable]
	public class NestedCustomObject : ICustomSerialization
	{
		public SimpleCustomObject Child { get; set; }

		public NestedCustomObject() { }

		protected NestedCustomObject(List<CustomSerializationData> data)
		{
			foreach (var entry in data)
			{
				if (entry.Name == "Child") Child = (SimpleCustomObject)entry.Value;
			}
		}

		public List<CustomSerializationData> GetSerializationData()
			=> Child != null
				? new() { new() { Name = "Child", Type = typeof(SimpleCustomObject), Value = Child } }
				: new() { new() { Name = "Child", Type = typeof(SimpleCustomObject), Value = null } };
	}

	[Serializable]
	public class CustomObjectWithNulls : ICustomSerialization
	{
		public int Id { get; private set; }
		public string Name { get; private set; }

		public CustomObjectWithNulls() { }

		public CustomObjectWithNulls(int id, string name)
		{
			Id = id;
			Name = name;
		}

		protected CustomObjectWithNulls(List<CustomSerializationData> data)
		{
			foreach (var entry in data)
			{
				if (entry.Name == "Id") Id = (int)entry.Value;
				else if (entry.Name == "Name") Name = (string)entry.Value;
			}
		}

		public List<CustomSerializationData> GetSerializationData()
			=> new()
			{
				new() { Name = "Id", Type = typeof(int), Value = Id },
				new() { Name = "Name", Type = typeof(string), Value = Name }
			};
	}

	[Serializable]
	public class NodeWithCustomSerialization : ICustomSerialization
	{
		public string Name { get; set; }
		public NodeWithCustomSerialization Parent { get; set; }
		public NodeWithCustomSerialization Child { get; set; }

		public NodeWithCustomSerialization() { }

		public NodeWithCustomSerialization(string name)
		{
			Name = name;
		}

		protected NodeWithCustomSerialization(List<CustomSerializationData> data)
		{
			foreach (var entry in data)
			{
				if (entry.Name == "Name") Name = (string)entry.Value;
				else if (entry.Name == "Parent" && entry.Value is NodeWithCustomSerialization parent) Parent = parent;
				else if (entry.Name == "Child" && entry.Value is NodeWithCustomSerialization child) Child = child;
			}
		}

		public List<CustomSerializationData> GetSerializationData()
			=> new()
			{
				new() { Name = "Name", Type = typeof(string), Value = Name },
				new() { Name = "Parent", Type = typeof(NodeWithCustomSerialization), Value = Parent },
				new() { Name = "Child", Type = typeof(NodeWithCustomSerialization), Value = Child }
			};
	}

	[Serializable]
	public class CustomObjectWithoutConstructor : ICustomSerialization
	{
		public int Id { get; set; } = 42;

		public List<CustomSerializationData> GetSerializationData()
			=> new() { new() { Name = "Id", Type = typeof(int), Value = Id } };
	}

	[Serializable]
	public class CustomObjectWithComplexTypes : ICustomSerialization
	{
		public int Id { get; set; }
		public List<int> Values { get; set; } = [];
		public Dictionary<string, object> Metadata { get; set; } = [];

		public CustomObjectWithComplexTypes() { }

		protected CustomObjectWithComplexTypes(List<CustomSerializationData> data)
		{
			foreach (var entry in data)
			{
				if (entry.Name == "Id") Id = (int)entry.Value;
				else if (entry.Name == "Values") Values = (List<int>)entry.Value;
				else if (entry.Name == "Metadata") Metadata = (Dictionary<string, object>)entry.Value;
			}
		}

		public List<CustomSerializationData> GetSerializationData()
			=> new()
			{
				new() { Name = "Id", Type = typeof(int), Value = Id },
				new() { Name = "Values", Type = typeof(List<int>), Value = Values },
				new() { Name = "Metadata", Type = typeof(Dictionary<string, object>), Value = Metadata }
			};
	}
}