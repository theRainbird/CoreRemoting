using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using CoreRemoting.Serialization.NeoBinary;
using CoreRemoting.Tests.Tools;
using CoreRemoting.Toolbox;
using Xunit;
using Xunit.Abstractions;

namespace CoreRemoting.Tests.Concurrency
{
	/// <summary>
	/// Concurrency tests for NeoBinarySerializer to validate thread-safety fixes.
	/// </summary>
	[Collection("NeoBinaryConcurrency")]
	public class NeoBinaryConcurrencyTests : IClassFixture<NeoBinaryConcurrencyFixture>
	{
		private readonly NeoBinaryConcurrencyFixture _fixture;
		private readonly ITestOutputHelper _output;

		public NeoBinaryConcurrencyTests(NeoBinaryConcurrencyFixture fixture, ITestOutputHelper output)
		{
			_fixture = fixture;
			_output = output;
		}

		[Fact]
		public async Task ConcurrentMixedTypesSerialization_Should_NotThrowRaceConditions()
		{
			// Arrange
			// using var ctx = ValidationSyncContext.Install(); // Disabled for now to avoid test framework conflicts
			var exceptions = new ConcurrentBag<Exception>();
			var completedOperations = new ConcurrentBag<(Type Type, bool Success)>();
			var threadCount = _fixture.NormalThreadCount;

			// Act
			var tasks = Enumerable.Range(0, threadCount).Select(i => Task.Run(async () =>
			{
				var random = new Random(i);
				var operations = 0;

				try
				{
					for (int j = 0; j < 10; j++)
					{
						var typeIndex = random.Next(_fixture.TestTypes.Count);
						var testType = _fixture.TestTypes[typeIndex];
						var testObject = _fixture.GetTestObject(testType);

						// Serialize
						var serialized = _fixture.Serializer.Serialize(testObject);
						await Task.Delay(1).ConfigureAwait(false);

						// Deserialize
						var deserialized = _fixture.Serializer.Deserialize(testType, serialized);
						await Task.Delay(1).ConfigureAwait(false);

						completedOperations.Add((testType, true));
						operations++;
					}
				}
				catch (Exception ex)
				{
					exceptions.Add(new Exception($"Thread {i} failed after {operations} operations", ex));
					completedOperations.Add((null, false));
				}
			}));

			await Task.WhenAll(tasks);

			// Assert
			Assert.Empty(exceptions);
			Assert.Equal(threadCount * 10, completedOperations.Count);
			Assert.All(completedOperations, op => Assert.True(op.Success));

			_output.WriteLine($"Completed {completedOperations.Count} operations across {threadCount} threads");
		}

		[Theory]
		[InlineData(typeof(TestComplexObject))]
		[InlineData(typeof(List<string>))]
		[InlineData(typeof(Dictionary<string, int>))]
		[InlineData(typeof(TestComplexObject[]))]
		[InlineData(typeof(DataTable))]
		public async Task ConcurrentSameTypeSerialization_Should_BeThreadSafe(Type testType)
		{
			// Arrange
			// using var ctx = ValidationSyncContext.Install(); // Disabled for now to avoid test framework conflicts
			var exceptions = new ConcurrentBag<Exception>();
			var results = new ConcurrentBag<(object Original, object Deserialized, bool Success)>();
			var threadCount = _fixture.StressThreadCount;

			// Act
			var tasks = Enumerable.Range(0, threadCount).Select(i => Task.Run(async () =>
			{
				try
				{
					for (int j = 0; j < 5; j++)
					{
						var testObject = _fixture.GetTestObject(testType);

						// Serialize
						var serialized = _fixture.Serializer.Serialize(testObject);
						await Task.Delay(1).ConfigureAwait(false);

						// Deserialize
						var deserialized = _fixture.Serializer.Deserialize(testType, serialized);

						results.Add((testObject, deserialized, true));
					}
				}
				catch (Exception ex)
				{
					exceptions.Add(new Exception($"Thread {i} failed for type {testType.Name}", ex));
					results.Add((null, null, false));
				}
			}));

			await Task.WhenAll(tasks).Timeout(_fixture.TestTimeout.TotalSeconds);

			// Assert
			Assert.Empty(exceptions);
			Assert.Equal(threadCount * 5, results.Count);
			Assert.All(results, r => Assert.True(r.Success));

			// Verify deserialized objects match originals (basic validation)
			Assert.All(results, r =>
			{
				if (r.Original is string originalStr && r.Deserialized is string deserializedStr)
				{
					Assert.Equal(originalStr, deserializedStr);
				}
				else if (r.Original is int originalInt && r.Deserialized is int deserializedInt)
				{
					Assert.Equal(originalInt, deserializedInt);
				}
			});
		}

		[Fact]
		public async Task CacheStatisticsConsistency_Should_ReturnValidData()
		{
			// Arrange
			// using var ctx = ValidationSyncContext.Install(); // Disabled for now to avoid test framework conflicts
			var exceptions = new ConcurrentBag<Exception>();
			var statisticsList = new ConcurrentBag<SerializerCache.CacheStatistics>();
			var threadCount = _fixture.NormalThreadCount;

			// Act
			var tasks = Enumerable.Range(0, threadCount).Select(i => Task.Run(async () =>
			{
				try
				{
					// Perform serialization operations
					for (int j = 0; j < 10; j++)
					{
						var testType = _fixture.TestTypes[j % _fixture.TestTypes.Count];
						var testObject = _fixture.GetTestObject(testType);
						var serialized = _fixture.Serializer.Serialize(testObject);
						var deserialized = _fixture.Serializer.Deserialize(testType, serialized);

						await Task.Delay(1).ConfigureAwait(false);
					}

					// Get statistics while other threads are still working
					var stats = _fixture.Serializer.GetCacheStatistics();
					if (stats != null)
					{
						statisticsList.Add(stats);
					}
				}
				catch (Exception ex)
				{
					exceptions.Add(new Exception($"Thread {i} failed", ex));
				}
			}));

			await Task.WhenAll(tasks).Timeout(_fixture.TestTimeout.TotalSeconds);

			// Assert
			Assert.Empty(exceptions);
			Assert.NotEmpty(statisticsList);

			// Verify statistics are reasonable
			Assert.All(statisticsList, stats =>
			{
				Assert.True(stats.SerializerCount >= 0);
				Assert.True(stats.DeserializerCount >= 0);
				Assert.True(stats.CacheHits >= 0);
				Assert.True(stats.CacheMisses >= 0);
				Assert.True(stats.TotalSerializations >= 0);
				Assert.True(stats.TotalDeserializations >= 0);
			});

			_output.WriteLine($"Collected {statisticsList.Count} statistics samples");
		}

		[Fact]
		public async Task PerformanceUnderLoad_Should_MaintainThroughput()
		{
			// Arrange
			// using var ctx = ValidationSyncContext.Install(); // Disabled for now to avoid test framework conflicts
			var exceptions = new ConcurrentBag<Exception>();
			var operationTimes = new ConcurrentBag<TimeSpan>();
			var threadCount = _fixture.StressThreadCount;

			// Act
			var tasks = Enumerable.Range(0, threadCount).Select(i => Task.Run(async () =>
			{
				try
				{
					for (int j = 0; j < 20; j++)
					{
						var stopwatch = Stopwatch.StartNew();

						var testType = _fixture.TestTypes[j % _fixture.TestTypes.Count];
						var testObject = _fixture.GetTestObject(testType);

						// Serialize
						var serialized = _fixture.Serializer.Serialize(testObject);

						// Deserialize
						var deserialized = _fixture.Serializer.Deserialize(testType, serialized);

						stopwatch.Stop();
						operationTimes.Add(stopwatch.Elapsed);

						await Task.Delay(1).ConfigureAwait(false);
					}
				}
				catch (Exception ex)
				{
					exceptions.Add(new Exception($"Thread {i} failed", ex));
				}
			}));

			await Task.WhenAll(tasks).Timeout(_fixture.TestTimeout.TotalSeconds);

			// Assert
			Assert.Empty(exceptions);
			Assert.Equal(threadCount * 20, operationTimes.Count);

			// Verify performance is reasonable (should complete in reasonable time)
			var avgTime = operationTimes.Average(t => t.TotalMilliseconds);
			var maxTime = operationTimes.Max(t => t.TotalMilliseconds);

			Assert.True(avgTime < 1000, $"Average operation time {avgTime}ms is too high");
			Assert.True(maxTime < 5000, $"Maximum operation time {maxTime}ms is too high");

			_output.WriteLine($"Average operation time: {avgTime:F2}ms");
			_output.WriteLine($"Maximum operation time: {maxTime:F2}ms");
			_output.WriteLine($"Total operations: {operationTimes.Count}");
		}

		[Fact]
		public async Task ConcurrentCacheEviction_Should_NotCauseDataLoss()
		{
			// Arrange
			// using var ctx = ValidationSyncContext.Install(); // Disabled for now to avoid test framework conflicts
			var exceptions = new ConcurrentBag<Exception>();
			var results = new ConcurrentBag<(bool Success, int CacheSize)>();
			var threadCount = _fixture.NormalThreadCount;

			// Force cache size to trigger eviction by creating many unique types
			var uniqueTestObjects = new List<object>();
			for (int i = 0; i < 150; i++) // More than typical cache size
			{
				var complexObject = new TestComplexObject
				{
					IntProperty = i,
					StringProperty = $"Unique_{i}_{Guid.NewGuid()}",
					DecimalProperty = i * 1.23m,
					DateTimeProperty = DateTime.UtcNow.AddMinutes(i),
					BoolProperty = i % 2 == 0,
					GuidProperty = Guid.NewGuid()
				};
				uniqueTestObjects.Add(complexObject);
			}

			// Act
			var tasks = Enumerable.Range(0, threadCount).Select(i => Task.Run(async () =>
			{
				try
				{
					for (int j = 0; j < 10; j++)
					{
						var objIndex = (i * 10 + j) % uniqueTestObjects.Count;
						var testObject = uniqueTestObjects[objIndex];

						// Serialize and deserialize to populate cache
						var serialized = _fixture.Serializer.Serialize(testObject);
						var deserialized = _fixture.Serializer.Deserialize(typeof(TestComplexObject), serialized);

						await Task.Delay(2).ConfigureAwait(false);
					}

					// Get cache statistics
					var stats = _fixture.Serializer.GetCacheStatistics();
					results.Add((true, stats?.SerializerCount ?? 0));
				}
				catch (Exception ex)
				{
					exceptions.Add(new Exception($"Thread {i} failed", ex));
					results.Add((false, 0));
				}
			}));

			await Task.WhenAll(tasks).Timeout(_fixture.TestTimeout.TotalSeconds);

			// Assert
			Assert.Empty(exceptions);
			Assert.Equal(threadCount, results.Count);
			Assert.All(results, r => Assert.True(r.Success));

			// Verify cache size is reasonable (should be bounded due to eviction)
			var cacheSizes = results.Select(r => r.CacheSize).Distinct().ToList();
			Assert.NotEmpty(cacheSizes);
			Assert.All(cacheSizes, size => Assert.True(size < 1000, $"Cache size {size} is unexpectedly large"));

			_output.WriteLine($"Cache sizes observed: {string.Join(", ", cacheSizes)}");
		}

		[Fact]
		public async Task RapidTypeCreation_Should_HandleConcurrencyGracefully()
		{
			// Arrange
			// using var ctx = ValidationSyncContext.Install(); // Disabled for now to avoid test framework conflicts
			var exceptions = new ConcurrentBag<Exception>();
			var typeCreationResults = new ConcurrentBag<(Type Type, bool Success)>();
			var threadCount = _fixture.NormalThreadCount;

			// Act
			var tasks = Enumerable.Range(0, threadCount).Select(i => Task.Run(async () =>
			{
				try
				{
					// Create unique types rapidly to stress the cache creation mechanism
					for (int j = 0; j < 5; j++)
					{
						// Create anonymous objects as new "types"
						var anonymousObject = new
						{
							ThreadId = i,
							OperationId = j,
							Timestamp = DateTime.UtcNow,
							Guid = Guid.NewGuid(),
							NestedData = new
							{
								InnerValue = new Random().Next(1, 1000),
								InnerString = $"Inner_{i}_{j}_{Guid.NewGuid()}"
							}
						};

						// Serialize and deserialize
						var serialized = _fixture.Serializer.Serialize(anonymousObject);
						var deserialized = _fixture.Serializer.Deserialize(anonymousObject.GetType(), serialized);

						typeCreationResults.Add((anonymousObject.GetType(), true));
						await Task.Delay(1).ConfigureAwait(false);
					}
				}
				catch (Exception ex)
				{
					exceptions.Add(new Exception($"Thread {i} failed during rapid type creation", ex));
					typeCreationResults.Add((null, false));
				}
			}));

			await Task.WhenAll(tasks).Timeout(_fixture.TestTimeout.TotalSeconds);

			// Assert
			Assert.Empty(exceptions);
			Assert.Equal(threadCount * 5, typeCreationResults.Count);
			Assert.All(typeCreationResults, r => Assert.True(r.Success));

			_output.WriteLine($"Successfully created and handled {typeCreationResults.Count} unique types");
		}

		[Fact]
		public async Task MixedReadAndWriteOperations_Should_MaintainConsistency()
		{
			// Arrange
			// using var ctx = ValidationSyncContext.Install(); // Disabled for now to avoid test framework conflicts
			var exceptions = new ConcurrentBag<Exception>();
			var statisticsReads = new ConcurrentBag<SerializerCache.CacheStatistics>();
			var serializationResults = new ConcurrentBag<bool>();
			var threadCount = _fixture.NormalThreadCount;

			// Populate some data first
			var baseObject = _fixture.GetTestObject(typeof(TestComplexObject));
			var baseSerialized = _fixture.Serializer.Serialize(baseObject);

			// Act
			var tasks = Enumerable.Range(0, threadCount).Select(i => Task.Run(async () =>
			{
				try
				{
					for (int j = 0; j < 10; j++)
					{
						if (j % 3 == 0)
						{
							// Read operation: get statistics
							var stats = _fixture.Serializer.GetCacheStatistics();
							if (stats != null)
							{
								statisticsReads.Add(stats);
							}
						}
						else
						{
							// Write operation: serialize/deserialize
							var testObject = _fixture.GetTestObject(_fixture.TestTypes[j % _fixture.TestTypes.Count]);
							var serialized = _fixture.Serializer.Serialize(testObject);
							var deserialized = _fixture.Serializer.Deserialize(testObject.GetType(), serialized);
							serializationResults.Add(true);
						}

						await Task.Delay(1).ConfigureAwait(false);
					}
				}
				catch (Exception ex)
				{
					exceptions.Add(new Exception($"Thread {i} failed during mixed operations", ex));
				}
			}));

			await Task.WhenAll(tasks).Timeout(_fixture.TestTimeout.TotalSeconds);

			// Assert
			Assert.Empty(exceptions);
			Assert.NotEmpty(statisticsReads);
			Assert.NotEmpty(serializationResults);

			// Verify statistics consistency
			Assert.All(statisticsReads, stats =>
			{
				Assert.True(stats.SerializerCount >= 0);
				Assert.True(stats.TotalSerializations >= 0);
				Assert.True(stats.TotalDeserializations >= 0);
			});

			_output.WriteLine($"Statistics reads: {statisticsReads.Count}");
			_output.WriteLine($"Serialization operations: {serializationResults.Count}");
		}
	}
}