using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using CoreRemoting.Serialization.Bson;
using CoreRemoting.Serialization.Binary;
using CoreRemoting.Serialization.NeoBinary;
using CoreRemoting.Benchmark;

BenchmarkRunner.Run<SerializationBenchmark>();
BenchmarkRunner.Run<RpcBenchmark>();
BenchmarkRunner.Run<SessionKeyPairBenchmark>();
