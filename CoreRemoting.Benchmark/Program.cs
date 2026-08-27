using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using CoreRemoting.Serialization.Bson;
using CoreRemoting.Serialization.Binary;
using CoreRemoting.Serialization.NeoBinary;

namespace CoreRemoting.Benchmark
{
    class Program
    {
        static void Main(string[] args)
        {
            //var summary = BenchmarkRunner.Run<SerializationBenchmark>();
            BenchmarkRunner.Run<NullChannelBenchmark>();
            BenchmarkRunner.Run<NamedPipeBenchmark>();
            BenchmarkRunner.Run<WebsocketBenchmark>();
            BenchmarkRunner.Run<TcpBenchmark>();
        }
    }
}
