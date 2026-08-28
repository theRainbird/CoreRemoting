using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using CoreRemoting.Serialization.Bson;
using CoreRemoting.Serialization.Binary;
using CoreRemoting.Serialization.NeoBinary;
using CoreRemoting.Benchmark;

#if !NET9_0_OR_GREATER

// Serializers, including BinaryFormatter
BenchmarkRunner.Run<SerializationBenchmark>();

#endif

// RPC Channels
BenchmarkRunner.Run<RpcBenchmark>();

// RSA vs ECSDA
//BenchmarkRunner.Run<SessionKeyPairBenchmark>();
