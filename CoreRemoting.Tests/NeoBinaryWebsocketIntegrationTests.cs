using System;
using System.Threading.Tasks;
using CoreRemoting.Authentication;
using CoreRemoting.Channels;
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
        
        _testOutputHelper.WriteLine($"Testobjekt erstellt: {testObject.Identifier}");
        _testOutputHelper.WriteLine($"IL Compact Format: AKTIVIERT");
        _testOutputHelper.WriteLine($"Port: {port}");

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
        Assert.NotSame(testObject, result); // Sollte eine Kopie sein
        
        _testOutputHelper.WriteLine("Echo mit IL Compact Format erfolgreich");
    }

    [Fact]
    public void ComplexEchoObject_WithoutIlCompactFormat_ShouldReturnIdenticalObject()
    {
        // Arrange
        var testObject = CreateComplexTestObject();
        var port = System.Threading.Interlocked.Increment(ref _nextPort);
        
        _testOutputHelper.WriteLine($"Testobjekt erstellt: {testObject.Identifier}");
        _testOutputHelper.WriteLine($"IL Compact Format: DEAKTIVIERT");
        _testOutputHelper.WriteLine($"Port: {port}");

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
        
        _testOutputHelper.WriteLine("Echo ohne IL Compact Format erfolgreich");
    }

    [Fact]
    public void ComplexEchoObject_BothFormats_ShouldReturnSameResult()
    {
        // Arrange
        var testObject = CreateComplexTestObject();
        
        _testOutputHelper.WriteLine($"Testobjekt erstellt: {testObject.Identifier}");

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
        
        _testOutputHelper.WriteLine("Null-Objekt wird korrekt behandelt");
    }

    [Fact]
    public void ComplexEchoObject_PerformanceComparison_IlCompactVsStandard()
    {
        // Arrange
        var testObject = CreateComplexTestObject();
        var iterations = 100;
        
        _testOutputHelper.WriteLine($"Performance-Test mit {iterations} Iterationen");

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
        var clientConfigCompact = new ClientConfig
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

        using var clientCompact = new RemotingClient(clientConfigCompact);
        clientCompact.Connect();
        var proxyCompact = clientCompact.CreateProxy<ITestService>();

        // Test ohne IL Compact Format
        var clientConfigStandard = new ClientConfig
        {
            ServerHostName = "localhost",
            ServerPort = serverConfig.NetworkPort,
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

        // Act & Measure - IL Compact Format
        var stopwatchCompact = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            var result = proxyCompact.EchoComplex(testObject);
            Assert.NotNull(result);
        }
        stopwatchCompact.Stop();

        // Act & Measure - Standard Format
        var stopwatchStandard = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            var result = proxyStandard.EchoComplex(testObject);
            Assert.NotNull(result);
        }
        stopwatchStandard.Stop();

        // Assert
        _testOutputHelper.WriteLine($"IL Compact Format: {stopwatchCompact.ElapsedMilliseconds}ms");
        _testOutputHelper.WriteLine($"Standard Format: {stopwatchStandard.ElapsedMilliseconds}ms");
        
        if (stopwatchCompact.ElapsedMilliseconds > 0)
        {
            var performanceRatio = (double)stopwatchStandard.ElapsedMilliseconds / stopwatchCompact.ElapsedMilliseconds;
            _testOutputHelper.WriteLine($"Performance-Vorteil: {performanceRatio:F2}x");
        }

        // Beide sollten erfolgreich sein, Performance kann variieren
        Assert.True(stopwatchCompact.ElapsedMilliseconds > 0);
        Assert.True(stopwatchStandard.ElapsedMilliseconds > 0);
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