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
    }

    [MemoryDiagnoser]
    public class SerializationBenchmark
    {
        private TestObject _testObject = null!;

        [GlobalSetup]
        public void Setup()
        {
            AppContext.SetSwitch("System.Runtime.Serialization.EnableUnsafeBinaryFormatterSerialization", true);
            _testObject = new TestObject
            {
                Id = 123,
                Name = "Benchmark Test",
                Items = new List<string> { "item1", "item2", "item3" },
                Timestamp = DateTime.Now
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
    }

    class Program
    {
        static void Main(string[] args)
        {
            var summary = BenchmarkRunner.Run<SerializationBenchmark>();
        }
    }
}
