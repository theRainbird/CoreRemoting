using System.Collections;
using System.Collections.Generic;
using CoreRemoting.Serialization.NeoBinary;
using Xunit;

namespace CoreRemoting.Tests.Serialization.NeoBinary
{
	/// <summary>
	/// Cyclic object graphs where the cycle runs through a collection back to an object that is still
	/// being deserialized (the owner of the collection).
	/// </summary>
	public class NeoBinaryCollectionCycleTests
	{
		public class CycleNode
		{
			public string Name { get; set; }
			public List<CycleNode> Children { get; set; } = [];
			public CycleNode[] ChildArray { get; set; }
			public Dictionary<string, CycleNode> NodesByName { get; set; } = new();
			public Hashtable NodesByKey { get; set; } = new();
			public object[] Mixed { get; set; }
		}

		private static CycleNode RoundTrip(CycleNode original)
		{
			var serializer = new NeoBinarySerializerAdapter();
			return serializer.Deserialize<CycleNode>(serializer.Serialize(original));
		}

		[Fact]
		public void NeoBinarySerializerAdapter_should_restore_list_containing_its_owner()
		{
			var root = new CycleNode { Name = "root" };
			root.Children.Add(root);

			var deserialized = RoundTrip(root);

			Assert.Single(deserialized.Children);
			Assert.Same(deserialized, deserialized.Children[0]);
		}

		[Fact]
		public void NeoBinarySerializerAdapter_should_restore_cycle_through_two_lists()
		{
			var root = new CycleNode { Name = "root" };
			var child = new CycleNode { Name = "child" };
			root.Children.Add(child);
			child.Children.Add(root);

			var deserialized = RoundTrip(root);

			Assert.Single(deserialized.Children);
			Assert.Equal("child", deserialized.Children[0].Name);
			Assert.Single(deserialized.Children[0].Children);
			Assert.Same(deserialized, deserialized.Children[0].Children[0]);
		}

		[Fact]
		public void NeoBinarySerializerAdapter_should_restore_list_with_forward_and_backward_references()
		{
			var root = new CycleNode { Name = "root" };
			var first = new CycleNode { Name = "first" };
			var second = new CycleNode { Name = "second" };
			root.Children.AddRange([first, root, second, first]);
			first.Children.Add(second);
			second.Children.AddRange([root, first]);

			var deserialized = RoundTrip(root);

			Assert.Equal(4, deserialized.Children.Count);
			var deserializedFirst = deserialized.Children[0];
			var deserializedSecond = deserialized.Children[2];
			Assert.Equal("first", deserializedFirst.Name);
			Assert.Same(deserialized, deserialized.Children[1]);
			Assert.Equal("second", deserializedSecond.Name);
			Assert.Same(deserializedFirst, deserialized.Children[3]);
			Assert.Same(deserializedSecond, deserializedFirst.Children[0]);
			Assert.Same(deserialized, deserializedSecond.Children[0]);
			Assert.Same(deserializedFirst, deserializedSecond.Children[1]);
		}

		[Fact]
		public void NeoBinarySerializerAdapter_should_restore_array_element_referencing_its_owner()
		{
			var root = new CycleNode { Name = "root" };
			var child = new CycleNode { Name = "child", ChildArray = [root] };
			root.ChildArray = [child];

			var deserialized = RoundTrip(root);

			Assert.Single(deserialized.ChildArray);
			Assert.Equal("child", deserialized.ChildArray[0].Name);
			Assert.Same(deserialized, deserialized.ChildArray[0].ChildArray[0]);
		}

		[Fact]
		public void NeoBinarySerializerAdapter_should_restore_object_array_element_referencing_its_owner()
		{
			var root = new CycleNode { Name = "root" };
			root.Mixed = [1, "text", root];

			var deserialized = RoundTrip(root);

			Assert.Equal(1, deserialized.Mixed[0]);
			Assert.Equal("text", deserialized.Mixed[1]);
			Assert.Same(deserialized, deserialized.Mixed[2]);
		}

		[Fact]
		public void NeoBinarySerializerAdapter_should_restore_generic_dictionary_value_referencing_its_owner()
		{
			var root = new CycleNode { Name = "root" };
			var child = new CycleNode { Name = "child" };
			root.NodesByName["self"] = root;
			root.NodesByName["child"] = child;
			child.NodesByName["root"] = root;

			var deserialized = RoundTrip(root);

			Assert.Equal(2, deserialized.NodesByName.Count);
			Assert.Same(deserialized, deserialized.NodesByName["self"]);
			Assert.Equal("child", deserialized.NodesByName["child"].Name);
			Assert.Same(deserialized, deserialized.NodesByName["child"].NodesByName["root"]);
		}

		[Fact]
		public void NeoBinarySerializerAdapter_should_restore_hashtable_value_referencing_its_owner()
		{
			var root = new CycleNode { Name = "root" };
			root.NodesByKey["self"] = root;

			var deserialized = RoundTrip(root);

			Assert.Single(deserialized.NodesByKey);
			Assert.Same(deserialized, deserialized.NodesByKey["self"]);
		}
	}
}
