using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using CoreRemoting.Serialization.NeoBinary;

namespace CoreRemoting.Tests.Concurrency
{
	/// <summary>
	/// Test fixture providing shared infrastructure for NeoBinary concurrency tests.
	/// </summary>
	public class NeoBinaryConcurrencyFixture : IDisposable
	{
		/// <summary>
		/// Gets the shared serializer instance for testing.
		/// </summary>
		public NeoBinarySerializerAdapter Serializer { get; }

		/// <summary>
		/// Gets the list of representative test types for concurrency testing.
		/// </summary>
		public List<Type> TestTypes { get; }

		/// <summary>
		/// Gets the timeout for concurrent operations.
		/// </summary>
		public TimeSpan TestTimeout { get; }

		/// <summary>
		/// Gets the number of threads to use for stress testing.
		/// </summary>
		public int StressThreadCount { get; }

		/// <summary>
		/// Gets the number of threads to use for normal load testing.
		/// </summary>
		public int NormalThreadCount { get; }

		/// <summary>
		/// Gets test objects by type.
		/// </summary>
		public Dictionary<Type, Func<object>> TestObjectFactory { get; }

		public NeoBinaryConcurrencyFixture()
		{
			// Configure serializer for concurrency testing
			var config = new NeoBinarySerializerConfig
			{
				AllowUnknownTypes = true,
				EnableCompression = false // Disable compression for faster testing
			};
			Serializer = new NeoBinarySerializerAdapter(config);

			TestTimeout = TimeSpan.FromSeconds(30);
			StressThreadCount = 50;
			NormalThreadCount = 20;

			// Define representative test types that are likely to cause race conditions
			TestTypes = new List<Type>
			{
				// Primitive types
				typeof(string),
				typeof(int),
				typeof(decimal),

				// Complex objects
				typeof(TestComplexObject),
				typeof(TestNode),
				typeof(CircularRefClass),

				// Collections
				typeof(List<string>),
				typeof(Dictionary<string, int>),
				typeof(List<TestComplexObject>),
				typeof(Dictionary<string, TestComplexObject>),

				// Arrays
				typeof(TestComplexObject[]),
				typeof(string[]),

				// Nullable types
				typeof(int?),
				typeof(DateTime?),

				// Polymorphic scenarios
				typeof(ITestInterface),
				typeof(List<ITestInterface>),

				// Special cases
				typeof(DataTable),
				typeof(DataSet)
			};

			// Factory methods for creating test instances
			TestObjectFactory = new Dictionary<Type, Func<object>>
			{
				{ typeof(string), () => $"Test string {Guid.NewGuid()}" },
				{ typeof(int), () => new Random().Next(1, 1000000) },
				{ typeof(decimal), () => (decimal)new Random().NextDouble() * 1000000 },
				{ typeof(int?), () => (int?)new Random().Next(1, 1000000) },
				{ typeof(DateTime?), () => (DateTime?)DateTime.UtcNow.AddMinutes(new Random().Next(-1000, 1000)) },
				{ typeof(List<string>), () => CreateStringList() },
				{ typeof(Dictionary<string, int>), () => CreateStringIntDictionary() },
				{ typeof(List<TestComplexObject>), () => CreateComplexObjectList() },
				{ typeof(TestComplexObject[]), () => CreateComplexObjectArray() },
				{ typeof(string[]), () => CreateStringArray() },
				{ typeof(List<ITestInterface>), () => CreateInterfaceList() },
				{ typeof(ITestInterface), () => new TestImplementation() },
				{ typeof(DataTable), () => CreateDataTable() },
				{ typeof(DataSet), () => CreateDataSet() },
				{ typeof(TestComplexObject), () => CreateTestComplexObject() },
				{ typeof(TestNode), () => CreateTestNode() },
				{ typeof(CircularRefClass), () => CreateCircularRefClass() },
				{ typeof(Dictionary<string, TestComplexObject>), () => CreateComplexObjectDictionary() }
			};
		}

		/// <summary>
		/// Creates a test string list.
		/// </summary>
		private List<string> CreateStringList()
		{
			var random = new Random();
			var list = new List<string>();
			for (int i = 0; i < random.Next(5, 20); i++)
			{
				list.Add($"Item {i} - {Guid.NewGuid()}");
			}
			return list;
		}

		/// <summary>
		/// Creates a test string-int dictionary.
		/// </summary>
		private Dictionary<string, int> CreateStringIntDictionary()
		{
			var random = new Random();
			var dict = new Dictionary<string, int>();
			for (int i = 0; i < random.Next(3, 10); i++)
			{
				dict[$"Key_{i}_{Guid.NewGuid()}"] = random.Next(1, 1000);
			}
			return dict;
		}

		/// <summary>
		/// Creates a test complex object list.
		/// </summary>
		private List<TestComplexObject> CreateComplexObjectList()
		{
			var random = new Random();
			var list = new List<TestComplexObject>();
			for (int i = 0; i < random.Next(3, 8); i++)
			{
				list.Add(CreateTestComplexObject());
			}
			return list;
		}

		/// <summary>
		/// Creates a test complex object dictionary.
		/// </summary>
		private Dictionary<string, TestComplexObject> CreateComplexObjectDictionary()
		{
			var random = new Random();
			var dict = new Dictionary<string, TestComplexObject>();
			for (int i = 0; i < random.Next(2, 6); i++)
			{
				dict[$"Obj_{i}_{Guid.NewGuid()}"] = CreateTestComplexObject();
			}
			return dict;
		}

		/// <summary>
		/// Creates a test complex object array.
		/// </summary>
		private TestComplexObject[] CreateComplexObjectArray()
		{
			var random = new Random();
			var array = new TestComplexObject[random.Next(2, 5)];
			for (int i = 0; i < array.Length; i++)
			{
				array[i] = CreateTestComplexObject();
			}
			return array;
		}

		/// <summary>
		/// Creates a test string array.
		/// </summary>
		private string[] CreateStringArray()
		{
			var random = new Random();
			var array = new string[random.Next(3, 8)];
			for (int i = 0; i < array.Length; i++)
			{
				array[i] = $"Array item {i} - {Guid.NewGuid()}";
			}
			return array;
		}

		/// <summary>
		/// Creates a test interface list.
		/// </summary>
		private List<ITestInterface> CreateInterfaceList()
		{
			var random = new Random();
			var list = new List<ITestInterface>();
			for (int i = 0; i < random.Next(2, 5); i++)
			{
				list.Add(new TestImplementation());
			}
			return list;
		}

		/// <summary>
		/// Creates a test DataTable.
		/// </summary>
		private DataTable CreateDataTable()
		{
			var table = new DataTable($"TestTable_{Guid.NewGuid()}");
			table.Columns.Add("Id", typeof(int));
			table.Columns.Add("Name", typeof(string));
			table.Columns.Add("Value", typeof(decimal));

			var random = new Random();
			for (int i = 0; i < random.Next(2, 5); i++)
			{
				table.Rows.Add(i, $"Name_{i}_{Guid.NewGuid()}", (decimal)random.NextDouble() * 100);
			}
			return table;
		}

		/// <summary>
		/// Creates a test DataSet.
		/// </summary>
		private DataSet CreateDataSet()
		{
			var dataSet = new DataSet($"TestDataSet_{Guid.NewGuid()}");
			dataSet.Tables.Add(CreateDataTable());
			return dataSet;
		}

		/// <summary>
		/// Creates a test complex object.
		/// </summary>
		private TestComplexObject CreateTestComplexObject()
		{
			var random = new Random();
			return new TestComplexObject
			{
				IntProperty = random.Next(1, 100000),
				StringProperty = $"Test string {Guid.NewGuid()}",
				DecimalProperty = (decimal)random.NextDouble() * 1000000,
				DateTimeProperty = DateTime.UtcNow.AddMinutes(random.Next(-1000, 1000)),
				BoolProperty = random.Next(0, 2) == 1,
				GuidProperty = Guid.NewGuid(),
				NestedObject = new TestNestedObject
				{
					InnerString = $"Nested {Guid.NewGuid()}",
					InnerInt = random.Next(1, 1000)
				}
			};
		}

		/// <summary>
		/// Creates a test node with potential circular references.
		/// </summary>
		private TestNode CreateTestNode()
		{
			var random = new Random();
			var node = new TestNode
			{
				Id = random.Next(1, 10000),
				Name = $"Node {Guid.NewGuid()}",
				Value = random.Next(1, 1000)
			};
			return node;
		}

		/// <summary>
		/// Creates a circular reference test class.
		/// </summary>
		private CircularRefClass CreateCircularRefClass()
		{
			var obj = new CircularRefClass();
			obj.Name = $"Circular {Guid.NewGuid()}";
			obj.Value = new Random().Next(1, 1000);
			// Note: Don't set up circular reference here as it can cause infinite loops during serialization
			return obj;
		}

		/// <summary>
		/// Gets a test object of the specified type.
		/// </summary>
		/// <param name="type">Type of test object to create</param>
		/// <returns>Test object instance</returns>
		public object GetTestObject(Type type)
		{
			if (TestObjectFactory.TryGetValue(type, out var factory))
			{
				return factory();
			}
			throw new ArgumentException($"No factory defined for type {type.Name}");
		}

		public void Dispose()
		{
			// NeoBinarySerializerAdapter doesn't implement IDisposable yet
			// Serializer?.Dispose();
		}
	}

	// Helper test classes for concurrency testing

	public interface ITestInterface
	{
		string Name { get; set; }
		int Value { get; set; }
	}

	public class TestImplementation : ITestInterface
	{
		public string Name { get; set; }
		public int Value { get; set; }
	}

	public class TestComplexObject
	{
		public int IntProperty { get; set; }
		public string StringProperty { get; set; }
		public decimal DecimalProperty { get; set; }
		public DateTime DateTimeProperty { get; set; }
		public bool BoolProperty { get; set; }
		public Guid GuidProperty { get; set; }
		public TestNestedObject NestedObject { get; set; }
	}

	public class TestNestedObject
	{
		public string InnerString { get; set; }
		public int InnerInt { get; set; }
	}

	public class TestNode
	{
		public int Id { get; set; }
		public string Name { get; set; }
		public int Value { get; set; }
		public TestNode Parent { get; set; }
		public List<TestNode> Children { get; set; } = new();
	}

	public class CircularRefClass
	{
		public string Name { get; set; }
		public int Value { get; set; }
		public CircularRefClass Reference { get; set; }
	}
}