using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using CoreRemoting.Serialization.Bson;
using CoreRemoting.Serialization.Binary;
using CoreRemoting.Serialization.NeoBinary;

namespace CoreRemoting.Benchmark
{
    [Serializable]
    public class TestObject
    {
        public int Id { get; set; }
        public string Name { get; set; } = null!;
        public List<string> Items { get; set; } = new();
        public DateTime Timestamp { get; set; }
        public decimal Price { get; set; }
        public decimal TaxRate { get; set; }
        public decimal TotalValue { get; set; }
        public AddressInfo Address { get; set; } = new();
        public List<OrderInfo> Orders { get; set; } = new();
        public Dictionary<string, object> Metadata { get; set; } = new();
    }

    [Serializable]
    public class AddressInfo
    {
        public string Street { get; set; } = null!;
        public string City { get; set; } = null!;
        public string Country { get; set; } = null!;
        public string PostalCode { get; set; } = null!;
        public AddressType Type { get; set; }
    }

    [Serializable]
    public class OrderInfo
    {
        public int OrderId { get; set; }
        public string ProductName { get; set; } = null!;
        public decimal Amount { get; set; }
        public DateTime OrderDate { get; set; }
        public OrderStatus Status { get; set; }
        public List<OrderItem> Items { get; set; } = new();
    }

    [Serializable]
    public class OrderItem
    {
        public string ProductCode { get; set; } = null!;
        public string Description { get; set; } = null!;
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal TotalPrice { get; set; }
    }

    public enum AddressType
    {
        Home,
        Work,
        Other
    }

    public enum OrderStatus
    {
        Pending,
        Processing,
        Shipped,
        Delivered,
        Cancelled
    }

    [MemoryDiagnoser]
    public class SerializationBenchmark
    {
        private TestObject _testObject = null!;
        private TestObject[] _largeObjectArray = null!;

        [GlobalSetup]
        public void Setup()
        {
            AppContext.SetSwitch("System.Runtime.Serialization.EnableUnsafeBinaryFormatterSerialization", true);
            _testObject = CreateComplexTestObject();
            
            // Create large object array for testing
            _largeObjectArray = new TestObject[100];
            for (int i = 0; i < _largeObjectArray.Length; i++)
            {
                _largeObjectArray[i] = CreateComplexTestObject();
                _largeObjectArray[i].Id = i;
                _largeObjectArray[i].Name = $"Large Object {i:D3} with extended name for testing string pooling performance";
            }
        }

        private TestObject CreateComplexTestObject()
        {
            return new TestObject
            {
                Id = 123,
                Name = "Complex Benchmark Test Object with Long Name for String Pooling",
                Items = new List<string> { "item1", "item2", "item3", "item4", "item5" },
                Timestamp = DateTime.Now,
                Price = 123.456789m,
                TaxRate = 0.19m,
                TotalValue = 147.012345m,
                Address = new AddressInfo
                {
                    Street = "123 Benchmark Street",
                    City = "Performance City",
                    Country = "Optimization Land",
                    PostalCode = "12345-678",
                    Type = AddressType.Work
                },
                Orders = new List<OrderInfo>
                {
                    new OrderInfo
                    {
                        OrderId = 1001,
                        ProductName = "High Performance Product",
                        Amount = 999.99m,
                        OrderDate = DateTime.Now.AddDays(-5),
                        Status = OrderStatus.Shipped,
                        Items = new List<OrderItem>
                        {
                            new OrderItem
                            {
                                ProductCode = "PROD-001",
                                Description = "Premium Quality Item with Extended Description",
                                Quantity = 10,
                                UnitPrice = 99.999m,
                                TotalPrice = 999.99m
                            },
                            new OrderItem
                            {
                                ProductCode = "PROD-002",
                                Description = "Standard Quality Item",
                                Quantity = 5,
                                UnitPrice = 49.995m,
                                TotalPrice = 249.975m
                            }
                        }
                    },
                    new OrderInfo
                    {
                        OrderId = 1002,
                        ProductName = "Another Product",
                        Amount = 149.50m,
                        OrderDate = DateTime.Now.AddDays(-2),
                        Status = OrderStatus.Processing,
                        Items = new List<OrderItem>
                        {
                            new OrderItem
                            {
                                ProductCode = "PROD-003",
                                Description = "Budget Friendly Option",
                                Quantity = 3,
                                UnitPrice = 49.833m,
                                TotalPrice = 149.499m
                            }
                        }
                    }
                },
                Metadata = new Dictionary<string, object>
                {
                    ["CustomerType"] = "Premium",
                    ["Priority"] = "High",
                    ["DiscountRate"] = 0.15m,
                    ["SpecialInstructions"] = "Handle with care and expedite shipping",
                    ["Tags"] = new List<string> { "urgent", "premium", "express" },
                    ["CreatedDate"] = DateTime.Now.AddDays(-10),
                    ["LastModified"] = DateTime.Now.AddHours(-2)
                }
            };
        }

        [Benchmark]
        public byte[] NeoBinary_Serialize()
        {
            var serializer = new NeoBinarySerializerAdapter();
            return serializer.Serialize(_testObject);
        }

        [Benchmark]
        public byte[] BinaryFormatter_Serialize()
        {
            var serializer = new BinarySerializerAdapter();
            return serializer.Serialize(_testObject);
        }

        [Benchmark]
        public byte[] Bson_Serialize()
        {
            var serializer = new BsonSerializerAdapter();
            return serializer.Serialize(_testObject);
        }

        [Benchmark]
        public byte[] Hyperion_Serialize()
        {
            var serializer = new CoreRemoting.Serialization.Hyperion.HyperionSerializerAdapter();
            return serializer.Serialize(_testObject);
        }

        [Benchmark]
        public TestObject NeoBinary_Deserialize()
        {
            var serializer = new NeoBinarySerializerAdapter();
            var data = serializer.Serialize(_testObject);
            return serializer.Deserialize<TestObject>(data);
        }

        [Benchmark]
        public TestObject BinaryFormatter_Deserialize()
        {
            var serializer = new BinarySerializerAdapter();
            var data = serializer.Serialize(_testObject);
            return serializer.Deserialize<TestObject>(data);
        }

        [Benchmark]
        public TestObject Bson_Deserialize()
        {
            var serializer = new BsonSerializerAdapter();
            var data = serializer.Serialize(_testObject);
            return serializer.Deserialize<TestObject>(data);
        }

        [Benchmark]
        public TestObject Hyperion_Deserialize()
        {
            var serializer = new CoreRemoting.Serialization.Hyperion.HyperionSerializerAdapter();
            var data = serializer.Serialize(_testObject);
            return serializer.Deserialize<TestObject>(data);
        }

        // Concurrent serialization tests to test lock removal benefits
        [Benchmark]
        public void NeoBinary_Concurrent_Serialize()
        {
            var serializer = new NeoBinarySerializerAdapter();
            Parallel.For(0, 50, i =>
            {
                var data = serializer.Serialize(_testObject);
                // Prevent optimization
                GC.KeepAlive(data);
            });
        }

        [Benchmark]
        public void BinaryFormatter_Concurrent_Serialize()
        {
            var serializer = new BinarySerializerAdapter();
            Parallel.For(0, 50, i =>
            {
                var data = serializer.Serialize(_testObject);
                // Prevent optimization
                GC.KeepAlive(data);
            });
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
            var summary = BenchmarkRunner.Run<SerializationBenchmark>();
        }
    }
}
