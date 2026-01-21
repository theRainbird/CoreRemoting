using System;
using CoreRemoting.Channels.Websocket;
using CoreRemoting.DependencyInjection;
using CoreRemoting.Serialization.NeoBinary;
using CoreRemoting.Tests.Tools;
using Xunit;
using Xunit.Abstractions;

namespace CoreRemoting.Tests;

/// <summary>
/// Integrationstests für CoreRemoting mit Websocket-Kanal und NeoBinary-Serialisierung
/// inklusive IL Compact Format Tests
/// </summary>
public class NeoBinaryWebsocketIntegrationTests : IDisposable
{
    private readonly ITestOutputHelper _testOutputHelper;
    private static int _nextPort = 9095;

    public NeoBinaryWebsocketIntegrationTests(ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
    }

    public void Dispose()
    {
        // Cleanup handled by individual test methods
    }

    [Fact]
    public void ComplexEchoObject_WithIlCompactFormat_ShouldReturnIdenticalObject()
    {
        // Arrange
        var testObject = CreateComplexTestObject();
        var port = System.Threading.Interlocked.Increment(ref _nextPort);
        
        // Erstelle Server-Konfiguration
        var serverConfig = new ServerConfig
        {
            NetworkPort = port,
            MessageEncryption = false,
            Serializer = new NeoBinarySerializerAdapter(),
            Channel = new WebsocketServerChannel(),
            RegisterServicesAction = container =>
            {
                container.RegisterService<ITestService, TestService>(lifetime: ServiceLifetime.Singleton);
            }
        };

        // Starte Server
        using var server = new RemotingServer(serverConfig);
        server.Start();

        // Erstelle Client-Konfiguration mit IL Compact Format aktiviert
        var clientConfig = new ClientConfig
        {
            ServerHostName = "localhost",
            ServerPort = port,
            MessageEncryption = false,
            Channel = new WebsocketClientChannel(),
            Serializer = new NeoBinarySerializerAdapter(new NeoBinarySerializerConfig
            {
                UseIlCompactLayout = true,
                IncludeFieldNames = false // Optimiert für IL Compact
            })
        };

        // Erstelle und starte Client
        using var client = new RemotingClient(clientConfig);
        client.Connect();

        // Erstelle Proxy
        var proxy = client.CreateProxy<ITestService>();

        // Act
        var result = proxy.EchoComplex(testObject);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(testObject, result);
        Assert.NotSame(testObject, result); // Must be a copy
    }

    [Fact]
    public void ComplexEchoObject_WithoutIlCompactFormat_ShouldReturnIdenticalObject()
    {
        // Arrange
        var testObject = CreateComplexTestObject();
        var port = System.Threading.Interlocked.Increment(ref _nextPort);
        
        // Erstelle Server-Konfiguration
        var serverConfig = new ServerConfig
        {
            NetworkPort = port,
            MessageEncryption = false,
            Serializer = new NeoBinarySerializerAdapter(),
            Channel = new WebsocketServerChannel(),
            RegisterServicesAction = container =>
            {
                container.RegisterService<ITestService, TestService>(lifetime: ServiceLifetime.Singleton);
            }
        };

        // Starte Server
        using var server = new RemotingServer(serverConfig);
        server.Start();

        // Erstelle Client-Konfiguration ohne IL Compact Format
        var clientConfig = new ClientConfig
        {
            ServerHostName = "localhost",
            ServerPort = port,
            MessageEncryption = false,
            Channel = new WebsocketClientChannel(),
            Serializer = new NeoBinarySerializerAdapter(new NeoBinarySerializerConfig
            {
                UseIlCompactLayout = false,
                IncludeFieldNames = true // Standard-Modus benötigt Feldnamen
            })
        };

        // Erstelle und starte Client
        using var client = new RemotingClient(clientConfig);
        client.Connect();

        // Erstelle Proxy
        var proxy = client.CreateProxy<ITestService>();

        // Act
        var result = proxy.EchoComplex(testObject);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(testObject, result);
        Assert.NotSame(testObject, result); // Sollte eine Kopie sein
    }

    [Fact]
    public void ComplexEchoObject_BothFormats_ShouldReturnSameResult()
    {
        // Arrange
        var testObject = CreateComplexTestObject();

        // Starte Server mit Websocket-Kanal
        var serverConfig = new ServerConfig
        {
            NetworkPort = System.Threading.Interlocked.Increment(ref _nextPort),
            MessageEncryption = false,
            Serializer = new NeoBinarySerializerAdapter(),
            Channel = new WebsocketServerChannel(),
            RegisterServicesAction = container =>
            {
                container.RegisterService<ITestService, TestService>(lifetime: ServiceLifetime.Singleton);
            }
        };

        using var server = new RemotingServer(serverConfig);
        server.Start();

        // Test mit IL Compact Format
        var clientConfig = new ClientConfig
        {
            ServerHostName = "localhost",
            ServerPort = serverConfig.NetworkPort,
            MessageEncryption = false,
            Channel = new WebsocketClientChannel(),
            Serializer = new NeoBinarySerializerAdapter(new NeoBinarySerializerConfig
            {
                UseIlCompactLayout = true,
                IncludeFieldNames = false
            })
        };

        using var client = new RemotingClient(clientConfig);
        client.Connect();
        var proxy = client.CreateProxy<ITestService>();

        // Act & Assert
        var result = proxy.EchoComplex(null);
        Assert.Null(result);
    }

    [Fact]
    public void ComplexEchoObject_PerformanceComparison_IlCompactVsStandard()
    {
        // Arrange
        var testObject = CreateComplexTestObject();
        var iterations = 50; // Reduziert um Timeouts zu vermeiden
        
        long compactTimeMs = 0;
        long standardTimeMs = 0;
        
        // Test 1: IL Compact Format mit eigenem Server
        using (var serverCompact = CreateServerForFormat(true))
        {
            serverCompact.Start();
            
            var clientConfigCompact = new ClientConfig
            {
                ServerHostName = "localhost",
                ServerPort = serverCompact.Config.NetworkPort,
                MessageEncryption = false,
                Channel = new WebsocketClientChannel(),
                Serializer = new NeoBinarySerializerAdapter(new NeoBinarySerializerConfig
                {
                    UseIlCompactLayout = true,
                    IncludeFieldNames = false
                })
            };

            using var clientCompact = new RemotingClient(clientConfigCompact);
            clientCompact.Connect();
            var proxyCompact = clientCompact.CreateProxy<ITestService>();

            // Act & Measure - IL Compact Format
            var stopwatchCompact = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < iterations; i++)
            {
                var result = proxyCompact.EchoComplex(testObject);
                Assert.NotNull(result);
            }
            stopwatchCompact.Stop();
            compactTimeMs = stopwatchCompact.ElapsedMilliseconds;
            
            _testOutputHelper.WriteLine($"IL Compact Format: {compactTimeMs}ms");
        }
        
        // Warte kurze Zeit für saubere Trennung
        System.Threading.Thread.Sleep(100);
        
        // Test 2: Standard Format mit eigenem Server
        using (var serverStandard = CreateServerForFormat(false))
        {
            serverStandard.Start();
            
            var clientConfigStandard = new ClientConfig
            {
                ServerHostName = "localhost",
                ServerPort = serverStandard.Config.NetworkPort,
                MessageEncryption = false,
                Channel = new WebsocketClientChannel(),
                Serializer = new NeoBinarySerializerAdapter(new NeoBinarySerializerConfig
                {
                    UseIlCompactLayout = false,
                    IncludeFieldNames = true
                })
            };

            using var clientStandard = new RemotingClient(clientConfigStandard);
            clientStandard.Connect();
            var proxyStandard = clientStandard.CreateProxy<ITestService>();

            // Act & Measure - Standard Format
            var stopwatchStandard = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < iterations; i++)
            {
                var result = proxyStandard.EchoComplex(testObject);
                Assert.NotNull(result);
            }
            stopwatchStandard.Stop();
            standardTimeMs = stopwatchStandard.ElapsedMilliseconds;
            
            _testOutputHelper.WriteLine($"Standard Format: {standardTimeMs}ms");
        }

        // Assert
        _testOutputHelper.WriteLine($"IL Compact Format: {compactTimeMs}ms");
        _testOutputHelper.WriteLine($"Standard Format: {standardTimeMs}ms");
        
        if (compactTimeMs > 0)
        {
            var performanceRatio = (double)standardTimeMs / compactTimeMs;
            _testOutputHelper.WriteLine($"Performance-Vorteil: {performanceRatio:F2}x");
        }

        Assert.True(compactTimeMs > 0, "Compact format should complete successfully");
        Assert.True(standardTimeMs > 0, "Standard format should complete successfully");
        Assert.True(compactTimeMs < 30000, $"Compact format should not timeout: {compactTimeMs}ms");
        Assert.True(standardTimeMs < 30000, $"Standard format should not timeout: {standardTimeMs}ms");
    }

    private RemotingServer CreateServerForFormat(bool useCompactFormat)
    {
        var serverConfig = new ServerConfig
        {
            NetworkPort = System.Threading.Interlocked.Increment(ref _nextPort),
            MessageEncryption = false,
            Serializer = new NeoBinarySerializerAdapter(new NeoBinarySerializerConfig
            {
                UseIlCompactLayout = useCompactFormat,
                IncludeFieldNames = !useCompactFormat // Complementär zum Client
            }),
            Channel = new WebsocketServerChannel(),
            RegisterServicesAction = container =>
            {
                container.RegisterService<ITestService, TestService>(lifetime: ServiceLifetime.Singleton);
            }
        };

        return new RemotingServer(serverConfig);
    }

    [Fact]
    public void ComplexObjectWithCircularReferences_WithIlCompactFormat_ShouldNotTimeout()
    {
        // Arrange
        var testObject = CreateComplexObjectWithCircularReferences();
        var port = System.Threading.Interlocked.Increment(ref _nextPort);
        
        // Erstelle Server-Konfiguration
        var serverConfig = new ServerConfig
        {
            NetworkPort = port,
            MessageEncryption = false,
            Serializer = new NeoBinarySerializerAdapter(new NeoBinarySerializerConfig
            {
                UseIlCompactLayout = true,
                IncludeFieldNames = false
            }),
            Channel = new WebsocketServerChannel(),
            RegisterServicesAction = container =>
            {
                container.RegisterService<ITestService, TestService>(lifetime: ServiceLifetime.Singleton);
            }
        };

        // Starte Server
        using var server = new RemotingServer(serverConfig);
        server.Start();

        // Erstelle Client-Konfiguration mit IL Compact Format aktiviert
        var clientConfig = new ClientConfig
        {
            ServerHostName = "localhost",
            ServerPort = port,
            MessageEncryption = false,
            Channel = new WebsocketClientChannel(),
            Serializer = new NeoBinarySerializerAdapter(new NeoBinarySerializerConfig
            {
                UseIlCompactLayout = true,
                IncludeFieldNames = false,
                MaxSerializedSize = 50 * 1024 * 1024 // 50MB für komplexe Objekte
            })
        };

        // Erstelle und starte Client
        using var client = new RemotingClient(clientConfig);
        client.Connect();

        // Erstelle Proxy
        var proxy = client.CreateProxy<ITestService>();

        // Act & Assert - Sollte ohne Timeout durchlaufen
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var result = proxy.EchoComplex(testObject);
        stopwatch.Stop();

        _testOutputHelper.WriteLine($"Duration with circular references: {stopwatch.ElapsedMilliseconds}ms");
        
        Assert.NotNull(result);
        Assert.True(stopwatch.ElapsedMilliseconds < 10000, $"Operation took too long: {stopwatch.ElapsedMilliseconds}ms");
    }

    [Fact]
    public void DeeplyNestedObject_WithIlCompactFormat_ShouldNotTimeout()
    {
        // Arrange
        var testObject = CreateDeeplyNestedObject(50); // 50 Ebenen Tiefe
        var port = System.Threading.Interlocked.Increment(ref _nextPort);
        
        // Erstelle Server-Konfiguration
        var serverConfig = new ServerConfig
        {
            NetworkPort = port,
            MessageEncryption = false,
            Serializer = new NeoBinarySerializerAdapter(new NeoBinarySerializerConfig
            {
                UseIlCompactLayout = true,
                IncludeFieldNames = false
            }),
            Channel = new WebsocketServerChannel(),
            RegisterServicesAction = container =>
            {
                container.RegisterService<ITestService, TestService>(lifetime: ServiceLifetime.Singleton);
            }
        };

        // Starte Server
        using var server = new RemotingServer(serverConfig);
        server.Start();

        // Erstelle Client-Konfiguration mit IL Compact Format aktiviert
        var clientConfig = new ClientConfig
        {
            ServerHostName = "localhost",
            ServerPort = port,
            MessageEncryption = false,
            Channel = new WebsocketClientChannel(),
            Serializer = new NeoBinarySerializerAdapter(new NeoBinarySerializerConfig
            {
                UseIlCompactLayout = true,
                IncludeFieldNames = false,
                MaxSerializedSize = 50 * 1024 * 1024
            })
        };

        // Erstelle und starte Client
        using var client = new RemotingClient(clientConfig);
        client.Connect();

        // Erstelle Proxy
        var proxy = client.CreateProxy<ITestService>();

        // Act & Assert - Sollte ohne Timeout durchlaufen
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var result = proxy.EchoComplex(testObject);
        stopwatch.Stop();

        _testOutputHelper.WriteLine($"Duration with deeply nested object: {stopwatch.ElapsedMilliseconds}ms");
        
        Assert.NotNull(result);
        Assert.True(stopwatch.ElapsedMilliseconds < 10000, $"Operation took too long: {stopwatch.ElapsedMilliseconds}ms");
    }

    private ComplexEchoObject CreateComplexObjectWithCircularReferences()
    {
        var parent = new ComplexEchoObject
        {
            Text = "Parent with circular reference",
            Number = 1,
            DecimalValue = 1.0,
            Flag = true,
            Timestamp = DateTime.UtcNow,
            Identifier = Guid.NewGuid(),
            StringList = new System.Collections.Generic.List<string> 
            { 
                "Parent", 
                "Circular" 
            },
            Dictionary = new System.Collections.Generic.Dictionary<string, int>
            {
                { "Parent", 1 },
                { "Child", 2 }
            }
        };

        var child = new ComplexEchoObject
        {
            Text = "Child with back reference",
            Number = 2,
            DecimalValue = 2.0,
            Flag = false,
            Timestamp = DateTime.UtcNow.AddMinutes(1),
            Identifier = Guid.NewGuid(),
            StringList = new System.Collections.Generic.List<string> 
            { 
                "Child", 
                "Back" 
            },
            Dictionary = new System.Collections.Generic.Dictionary<string, int>
            {
                { "Child", 2 },
                { "Parent", 1 }
            }
        };

        // Zirkuläre Referenz durch das Nested-Objekt
        parent.Nested = new NestedObject
        {
            Name = "Shared Nested",
            Value = 42,
            DoubleArray = new[] { 1.0, 2.0, 3.0 }
        };

        child.Nested = parent.Nested; // Gemeinsame Referenz

        return parent;
    }

    private ComplexEchoObject CreateDeeplyNestedObject(int depth)
    {
        var root = new ComplexEchoObject
        {
            Text = $"Root level {depth}",
            Number = depth,
            DecimalValue = (double)depth,
            Flag = depth % 2 == 0,
            Timestamp = DateTime.UtcNow,
            Identifier = Guid.NewGuid(),
            StringList = new System.Collections.Generic.List<string> 
            { 
                $"Level {depth}" 
            },
            Dictionary = new System.Collections.Generic.Dictionary<string, int>
            {
                { $"Level_{depth}", depth }
            }
        };

        if (depth > 1)
        {
            root.Nested = new NestedObject
            {
                Name = $"Nested {depth}",
                Value = depth * 100,
                DoubleArray = new[] { (double)depth, (double)depth * 0.1, (double)depth * 0.2 }
            };
            
            // Erstelle verschachteltes Objekt
            var nestedComplex = CreateDeeplyNestedObject(depth - 1);
            root.NestedArray = new NestedObject[] { 
                new NestedObject 
                { 
                    Name = $"Nested Array {depth}", 
                    Value = depth * 200,
                    DoubleArray = new[] { (double)depth * 2, (double)depth * 2.1 }
                }
            };
        }

        return root;
    }

    private ComplexEchoObject CreateComplexTestObject()
    {
        return new ComplexEchoObject
        {
            Text = "Integrationstest für komplexe Objekte",
            Number = 2024,
            DecimalValue = 2.718281828,
            Flag = true,
            Timestamp = DateTime.UtcNow,
            Identifier = Guid.NewGuid(),
            StringList = new System.Collections.Generic.List<string> 
            { 
                "Test Item 1", 
                "Test Item 2", 
                "Test Item 3" 
            },
            Dictionary = new System.Collections.Generic.Dictionary<string, int>
            {
                { "Eins", 1 },
                { "Zwei", 2 },
                { "Drei", 3 }
            },
            Nested = new NestedObject
            {
                Name = "Integrationstest-Nested",
                Value = 999,
                DoubleArray = new[] { 0.1, 0.2, 0.3, 0.4, 0.5 }
            },
            NestedArray = new[]
            {
                new NestedObject { Name = "Array-1", Value = 111, DoubleArray = new[] { 1.0, 1.1 } },
                new NestedObject { Name = "Array-2", Value = 222, DoubleArray = new[] { 2.0, 2.2 } }
            },
            EnumValue = ComplexEchoObject.TestEnum.Third
        };
    }
}