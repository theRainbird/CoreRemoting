using System;
using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CoreRemoting.Channels.Quic;
using Xunit;

namespace CoreRemoting.Tests;

public class QuicRelatedTests
{
    private const string TestProtocol = "test";
    private const long DefaultStreamErrorCode = 0x0A;
    private const long DefaultCloseErrorCode = 0x0B;
    private const int MaxStreams = 100;

    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    [Fact]
    public async Task Quic_handshake_works()
    {
        var cert = CertificateHelper.GenerateSelfSigned("localhost");
        Assert.True(cert.HasPrivateKey, "Certificate must have private key");

        var serverConnectionOptions = new QuicServerConnectionOptions
        {
            DefaultStreamErrorCode = DefaultStreamErrorCode,
            DefaultCloseErrorCode = DefaultCloseErrorCode,
            MaxInboundBidirectionalStreams = MaxStreams,
            MaxInboundUnidirectionalStreams = MaxStreams,
            IdleTimeout = TimeSpan.FromMinutes(1),
            HandshakeTimeout = TimeSpan.FromSeconds(10),
            ServerAuthenticationOptions = new()
            {
                ServerCertificateSelectionCallback = (_, _) => cert,
                ApplicationProtocols = [new SslApplicationProtocol(TestProtocol)]
            }
        };

        var serverOptions = new QuicListenerOptions
        {
            ListenEndPoint = new IPEndPoint(IPAddress.Loopback, 0),
            ApplicationProtocols = [new SslApplicationProtocol(TestProtocol)],
            ConnectionOptionsCallback = (_, _, _) => ValueTask.FromResult(serverConnectionOptions)
        };

        await using var listener = await QuicListener.ListenAsync(serverOptions);
        var listenEndPoint = listener.LocalEndPoint;

        var clientOptions = new QuicClientConnectionOptions
        {
            RemoteEndPoint = listenEndPoint,
            DefaultStreamErrorCode = DefaultStreamErrorCode,
            DefaultCloseErrorCode = DefaultCloseErrorCode,
            MaxInboundBidirectionalStreams = MaxStreams,
            MaxInboundUnidirectionalStreams = MaxStreams,
            IdleTimeout = TimeSpan.FromMinutes(1),
            HandshakeTimeout = TimeSpan.FromSeconds(10),
            ClientAuthenticationOptions = new()
            {
                TargetHost = "localhost",
                RemoteCertificateValidationCallback = (_, _, _, _) => true,
                ApplicationProtocols = [new SslApplicationProtocol(TestProtocol)]
            }
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var clientTask = QuicConnection.ConnectAsync(clientOptions, cts.Token);
        var serverTask = listener.AcceptConnectionAsync(cts.Token);

        await using var client = await clientTask;
        await using var server = await serverTask;

        Assert.Equal(new SslApplicationProtocol(TestProtocol), client.NegotiatedApplicationProtocol);
        Assert.Equal(new SslApplicationProtocol(TestProtocol), server.NegotiatedApplicationProtocol);

        // Give msquic time to complete internal synchronization after handshake.
        // Required on Windows, where the connection may not be fully ready
        // for stream operations immediately after ConnectAsync returns.
        await Task.Delay(100);

        // IMPORTANT: Client opens the stream first and immediately sends data.
        // This ensures the server sees the stream when calling AcceptInboundStreamAsync.
        // Without this order, AcceptInboundStreamAsync hangs because the remote side
        // has not sent any data on the newly created stream yet, and the test
        // is aborted by the CancellationTokenSource.
        await using var clientStream = await client.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, cts.Token);

        var request = Encoding.UTF8.GetBytes("hello from client");
        await clientStream.WriteAsync(request, cts.Token);
        await clientStream.FlushAsync(cts.Token);

        // Now the server can accept the stream — data has already been sent.
        await using var serverStream = await server.AcceptInboundStreamAsync(cts.Token);

        // Read request data on the server (already in the buffer).
        var requestBuffer = new byte[request.Length];
        await serverStream.ReadAtLeastAsync(requestBuffer, requestBuffer.Length, cancellationToken: cts.Token);
        Assert.Equal(request, requestBuffer);

        // Server sends response.
        var response = Encoding.UTF8.GetBytes("hello from server");
        await serverStream.WriteAsync(response, cts.Token);
        await serverStream.FlushAsync(cts.Token);

        // Client reads response.
        var responseBuffer = new byte[response.Length];
        await clientStream.ReadAtLeastAsync(responseBuffer, responseBuffer.Length, cancellationToken: cts.Token);
        Assert.Equal(response, responseBuffer);

        // Explicitly signal end-of-stream on both sides for clean closure.
        clientStream.CompleteWrites();
        serverStream.CompleteWrites();
    }
}