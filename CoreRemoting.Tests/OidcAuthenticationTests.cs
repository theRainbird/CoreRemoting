using System;
using System.Linq;
using System.Net;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CoreRemoting.Authentication;
using CoreRemoting.Authentication.Oidc;
using CoreRemoting.DependencyInjection;
using CoreRemoting.Tests.Tools;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CoreRemoting.Tests;

[Collection("CoreRemoting")]
public class OidcAuthenticationTests
{
    private static int _nextPort = 9500;

    /// <summary>
    /// A mock OIDC identity provider that provides the openid-configuration discovery document and a JWKS.
    /// </summary>
    private sealed class MockIdP : IDisposable
    {
        private readonly HttpListener _listener;
        private readonly Thread _thread;

        public MockIdP(int port, RSA signingKey)
        {
            Port = port;
            SigningKey = signingKey;
            BaseUrl = $"http://localhost:{port}";

            var parameters = signingKey.ExportParameters(false);

            var jwksDocument = new JObject
            {
                ["keys"] = new JArray
                {
                    new JObject
                    {
                        ["kty"] = "RSA",
                        ["alg"] = "RS256",
                        ["use"] = "sig",
                        ["kid"] = "key-1",
                        ["n"] = EncodeBase64Url(parameters.Modulus),
                        ["e"] = EncodeBase64Url(parameters.Exponent),
                    },
                },
            };

            var discoveryDocument = new JObject
            {
                ["issuer"] = BaseUrl,
                ["jwks_uri"] = $"{BaseUrl}/jwks",
            };

            _listener = new HttpListener();
            _listener.Prefixes.Add($"{BaseUrl}/");

            _thread = new Thread(() => ListenLoop(jwksDocument.ToString(), discoveryDocument.ToString()))
            {
                IsBackground = true,
            };
        }

        public int Port { get; }

        public RSA SigningKey { get; }

        public string BaseUrl { get; }

        /// <summary>
        /// Starts listening and returns this instance (usability within object initializers).
        /// </summary>
        public MockIdP Start()
        {
            _listener.Start();
            _thread.Start();
            return this;
        }

        private void ListenLoop(string jwks, string discovery)
        {
            while (_listener.IsListening)
            {
                HttpListenerContext context;
                try
                {
                    context = _listener.GetContext();
                }
                catch (Exception)
                {
                    return; // listener was stopped
                }

                var path = context.Request.Url.AbsolutePath;

                string json;
                if (path == "/.well-known/openid-configuration")
                    json = discovery;
                else if (path == "/jwks")
                    json = jwks;
                else
                {
                    context.Response.StatusCode = 404;
                    context.Response.Close();
                    continue;
                }

                var rawData = Encoding.UTF8.GetBytes(json);
                context.Response.ContentType = "application/json";
                context.Response.ContentLength64 = rawData.Length;
                context.Response.OutputStream.Write(rawData, 0, rawData.Length);
                context.Response.Close();
            }
        }

        public void Dispose()
        {
            _listener.Stop();
            _listener.Close();
        }
    }

    // #region helpers

    private static string CreateToken(
        RSA signingKey,
        int port,
        string issuer = null,
        string subject = "test-user",
        string audience = "core-remoting",
        long expiryOffsetSeconds = 600,
        long notBeforeOffsetSeconds = -10,
        string kid = "key-1",
        string algorithm = "RS256")
    {
        var header = new JObject
        {
            ["alg"] = algorithm,
            ["kid"] = kid,
        };

        var payload = new JObject
        {
            ["iss"] = issuer ?? $"http://localhost:{port}",
            ["sub"] = subject,
            ["aud"] = audience,
            ["exp"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + expiryOffsetSeconds,
            ["nbf"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + notBeforeOffsetSeconds,
            ["roles"] = new JArray("admin", "user"),
        };

        var plainText = EncodeBase64Url(Encoding.UTF8.GetBytes(header.ToString())) + "."
            + EncodeBase64Url(Encoding.UTF8.GetBytes(payload.ToString()));

        var signature = signingKey.SignData(
            Encoding.UTF8.GetBytes(plainText), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        return $"{plainText}.{EncodeBase64Url(signature)}";
    }

    private static string EncodeBase64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static OidcOptions CreateOidcOptions(
        int port,
        Func<string, string, bool> stepUpValidator = null,
        bool negotiateNewSessionKey = false) =>
        new()
        {
            Issuer = $"http://localhost:{port}",
            AllowedAudiences = ["core-remoting"],
            StepUpValidator = stepUpValidator,
            NegotiateNewSessionKey = negotiateNewSessionKey,
        };

    private static async Task<AuthenticationResponseMessage> AuthenticateToken(
        IAuthenticationProvider provider, string token)
    {
        return await provider.Authenticate(new AuthenticationRequestMessage
        {
            Credentials =
            [
                new Credential { Name = "oidc_token", Value = token },
            ],
        });
    }

    private static RemotingServer StartOidcServer(int port, OidcOptions options, EventHandler<Exception> onServerError)
    {
        var config = new ServerConfig()
        {
            IsDefault = false,
            UniqueServerInstanceName = $"OidcAuthenticationTestServer_{port}",
            MessageEncryption = true,
            KeySize = 1024,
            NetworkPort = port,
            AuthenticationRequired = true,
            AuthenticationProvider = new OidcAuthenticationProvider(options),
            RegisterServicesAction = container =>
            {
                var testService = new TestService()
                {
                    TestMethodFake = arg => arg
                };

                container.RegisterService<ITestService>(
                    factoryDelegate: () => testService,
                    lifetime: ServiceLifetime.Singleton);
            }
        };

        var server = new RemotingServer(config);
        server.Error += onServerError;
        server.Start();

        // wait until the channel is actually listening (WatsonTcp binds asynchronously)
        var deadline = DateTime.Now.AddSeconds(10);
        while (!server.Channel.IsListening && DateTime.Now < deadline)
            Thread.Sleep(20);

        if (!server.Channel.IsListening)
            throw new Exception($"Server on port {port} did not start listening in time.");

        return server;
    }

    private static RemotingClient CreateOidcClient(
        int serverPort,
        Func<Task<string>> tokenAcquirer,
        Func<string, Task<string>> stepUpPrompt = null)
    {
        return new RemotingClient(new ClientConfig()
        {
            ConnectionTimeout = 0,
            MessageEncryption = true,
            KeySize = 1024,
            ServerHostName = "localhost",
            ServerPort = serverPort,
            KeepSessionAliveInterval = 0,
            Authenticator = new OidcAuthenticator(tokenAcquirer, stepUpPrompt),
        });
    }

    // #endregion

    // #region Direct provider tests (no remoting channel)

    [Fact]
    public async Task Valid_token_should_authenticate_and_populate_the_identity()
    {
        var key = RSA.Create(2048);
        using var mockIdP = new MockIdP(Interlocked.Increment(ref _nextPort), key).Start();
        var provider = new OidcAuthenticationProvider(CreateOidcOptions(mockIdP.Port));

        var response = await AuthenticateToken(provider, CreateToken(key, mockIdP.Port));

        Assert.True(response.IsCompleted);
        Assert.True(response.IsAuthenticated);
        Assert.NotNull(response.AuthenticatedIdentity);
        Assert.Equal("test-user", response.AuthenticatedIdentity.Name);
        Assert.Equal("OIDC", response.AuthenticatedIdentity.AuthenticationType);
        Assert.Equal(new[] { "admin", "user" }, response.AuthenticatedIdentity.Roles);
        Assert.Equal("test-user", response.AuthenticatedIdentity.Claims["sub"][0]);
        Assert.Equal(new[] { "core-remoting" }, response.AuthenticatedIdentity.Claims["aud"]);

        key.Dispose();
    }

    [Fact]
    public async Task Invalid_signature_should_be_rejected()
    {
        var jwksKey = RSA.Create(2048);
        using var mockIdP = new MockIdP(Interlocked.Increment(ref _nextPort), jwksKey).Start();
        var provider = new OidcAuthenticationProvider(CreateOidcOptions(mockIdP.Port));

        // the token is signed with a key that isn't part of the JWKS
        using var wrongKey = RSA.Create(2048);
        var response = await AuthenticateToken(provider, CreateToken(wrongKey, mockIdP.Port));

        Assert.False(response.IsAuthenticated);
        Assert.Contains("signature", response.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        jwksKey.Dispose();
    }

    [Fact]
    public async Task Expired_token_should_be_rejected()
    {
        var key = RSA.Create(2048);
        using var mockIdP = new MockIdP(Interlocked.Increment(ref _nextPort), key).Start();
        var provider = new OidcAuthenticationProvider(CreateOidcOptions(mockIdP.Port));

        var response = await AuthenticateToken(provider, CreateToken(key, mockIdP.Port, expiryOffsetSeconds: -600));

        Assert.False(response.IsAuthenticated);
        Assert.Contains("expired", response.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        key.Dispose();
    }

    [Fact]
    public async Task Token_before_its_not_before_time_should_be_rejected()
    {
        var key = RSA.Create(2048);
        using var mockIdP = new MockIdP(Interlocked.Increment(ref _nextPort), key).Start();
        var provider = new OidcAuthenticationProvider(CreateOidcOptions(mockIdP.Port));

        var response = await AuthenticateToken(provider, CreateToken(key, mockIdP.Port, notBeforeOffsetSeconds: 3600));

        Assert.False(response.IsAuthenticated);
        Assert.Contains("valid yet", response.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        key.Dispose();
    }

    [Fact]
    public async Task Wrong_issuer_should_be_rejected()
    {
        var key = RSA.Create(2048);
        using var mockIdP = new MockIdP(Interlocked.Increment(ref _nextPort), key).Start();
        var provider = new OidcAuthenticationProvider(CreateOidcOptions(mockIdP.Port));

        var response = await AuthenticateToken(
            provider, CreateToken(key, mockIdP.Port, issuer: "http://localhost.evil.example"));

        Assert.False(response.IsAuthenticated);
        Assert.Contains("issued by", response.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        key.Dispose();
    }

    [Fact]
    public async Task Wrong_audience_should_be_rejected()
    {
        var key = RSA.Create(2048);
        using var mockIdP = new MockIdP(Interlocked.Increment(ref _nextPort), key).Start();
        var provider = new OidcAuthenticationProvider(CreateOidcOptions(mockIdP.Port));

        var response = await AuthenticateToken(
            provider, CreateToken(key, mockIdP.Port, audience: "some-other-api"));

        Assert.False(response.IsAuthenticated);
        Assert.Contains("audience", response.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        key.Dispose();
    }

    [Fact]
    public async Task Unsupported_signature_algorithm_should_be_rejected()
    {
        using var key = RSA.Create(2048);
        using var mockIdP = new MockIdP(Interlocked.Increment(ref _nextPort), key).Start();
        var provider = new OidcAuthenticationProvider(CreateOidcOptions(mockIdP.Port));

        var response = await AuthenticateToken(provider, CreateToken(key, mockIdP.Port, algorithm: "HS256"));

        Assert.False(response.IsAuthenticated);
        Assert.Contains("algorithm", response.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Unknown_key_id_should_be_rejected()
    {
        using var key = RSA.Create(2048);
        using var mockIdP = new MockIdP(Interlocked.Increment(ref _nextPort), key).Start();
        var provider = new OidcAuthenticationProvider(CreateOidcOptions(mockIdP.Port));

        // the token references a 'kid' that doesn't exist within the JWKS (not rotated yet)
        var response = await AuthenticateToken(provider, CreateToken(key, mockIdP.Port, kid: "unknown-key"));

        Assert.False(response.IsAuthenticated);
        Assert.Contains("JWKS", response.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Step_up_code_without_pending_state_should_fail()
    {
        using var key = RSA.Create(2048);
        using var mockIdP = new MockIdP(Interlocked.Increment(ref _nextPort), key).Start();
        IAuthenticationProvider provider =
            new OidcAuthenticationProvider(CreateOidcOptions(mockIdP.Port, stepUpValidator: (name, code) => true));

        // a 'step_up_code' is only allowed after a successful token validation within the same session
        var response = await provider.Authenticate(new AuthenticationRequestMessage
        {
            Credentials =
            [
                new Credential { Name = "step_up_code", Value = "123456" },
            ],
        });

        Assert.True(response.IsCompleted);
        Assert.False(response.IsAuthenticated);
        Assert.Contains("pending", response.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Fallback_provider_should_be_used_when_no_token_was_provided()
    {
        var invocationCount = 0;
        IAuthenticationProvider fallback = new CountingFallbackAuthenticationProvider(() => invocationCount++);
        var port = Interlocked.Increment(ref _nextPort);

        using var mockIdP = new MockIdP(port, RSA.Create(2048)).Start();
        IAuthenticationProvider provider = new OidcAuthenticationProvider(CreateOidcOptions(port), fallback);

        var response = await provider.Authenticate(new AuthenticationRequestMessage
        {
            Credentials = Array.Empty<Credential>(),
        });

        Assert.Equal(1, invocationCount);
        Assert.True(response.IsAuthenticated);
        Assert.Equal("fallback-user", response.AuthenticatedIdentity.Name);
    }

    // #endregion

    // #region End-to-end tests (remoting channel)

    [Fact]
    public async Task Client_should_be_able_to_authenticate_with_token_and_step_up_code()
    {
        var key = RSA.Create(2048);
        var port = Interlocked.Increment(ref _nextPort);
        using var mockIdP = new MockIdP(port, key).Start();

        Exception lastServerError = null;
        var onError = new EventHandler<Exception>((sender, exception) => { lastServerError = exception; });
        var serverPort = Interlocked.Increment(ref _nextPort);

        using (var server = StartOidcServer(
                port: serverPort,
                options: CreateOidcOptions(port, stepUpValidator: (name, code) =>
                    name == "test-user" && code == "123456"),
                onServerError: onError))
        {
            using var client = CreateOidcClient(
                serverPort,
                tokenAcquirer: async () => await Task.FromResult(CreateToken(key, port)),
                stepUpPrompt: async type => await Task.FromResult("123456"));

            await client.ConnectAsync();

            Assert.Equal("test-user", client.Identity.Name);
            Assert.Equal("OIDC", client.Identity.AuthenticationType);

            var proxy = client.CreateProxy<ITestService>();
            Assert.Equal("ping", (string)proxy.TestMethod("ping"));

            await client.DisconnectAsync();

            server.Stop();
        }

        if (lastServerError != null)
            throw lastServerError;
    }

    [Fact]
    public async Task Client_should_fail_when_the_step_up_code_was_rejected()
    {
        var key = RSA.Create(2048);
        var port = Interlocked.Increment(ref _nextPort);
        using var mockIdP = new MockIdP(port, key).Start();

        Exception lastServerError = null;

        var serverPort = Interlocked.Increment(ref _nextPort);

        using (var server = StartOidcServer(
                port: serverPort,
                options: CreateOidcOptions(port, stepUpValidator: (name, code) => code == "123456"),
                onServerError: (sender, exception) => lastServerError = exception))
        {
            using var client = CreateOidcClient(
                serverPort,
                tokenAcquirer: async () => await Task.FromResult(CreateToken(key, port)),
                stepUpPrompt: async type => await Task.FromResult("000000")); // wrong code

            var exception = await Assert.ThrowsAsync<SecurityException>(() => client.ConnectAsync());
            Assert.Contains("step-up", exception.Message, StringComparison.OrdinalIgnoreCase);

            server.Stop();
        }

        if (lastServerError != null)
            throw lastServerError;
    }

    [Fact(Skip = "Not ready yet")]
    public async Task Session_key_should_be_renegotiated_when_enabled()
    {
        var key = RSA.Create(2048);
        var port = Interlocked.Increment(ref _nextPort);
        using var mockIdP = new MockIdP(port, key).Start();

        Exception lastServerError = null;

        var serverPort = Interlocked.Increment(ref _nextPort);

        using (var server = StartOidcServer(
                port: serverPort,
                options: CreateOidcOptions(port, negotiateNewSessionKey: true),
                onServerError: (sender, exception) => lastServerError = exception))
        {
            using var client = CreateOidcClient(
                serverPort,
                tokenAcquirer: async () => await Task.FromResult(CreateToken(key, port)));

            await client.ConnectAsync();

            var session = server.SessionRepository.Sessions.Single();

            // the shared secret is no longer derived from the session id, but the random key that was negotiated during authentication
            Assert.NotNull(session.SharedSecret);
            Assert.Equal(32, session.SharedSecret.Length);
            Assert.NotEqual(session.SessionId.ToByteArray(), session.SharedSecret);

            var proxy = client.CreateProxy<ITestService>();
            Assert.Equal("ping", (string)proxy.TestMethod("ping"));

            await client.DisconnectAsync();

            server.Stop();
        }

        if (lastServerError != null)
            throw lastServerError;
    }

    // #endregion

    private class CountingFallbackAuthenticationProvider : IAuthenticationProvider
    {
        private readonly Func<int> _invocationCounter;

        public CountingFallbackAuthenticationProvider(Func<int> invocationCounter)
        {
            _invocationCounter = invocationCounter;
        }

        public Task<AuthenticationResponseMessage> Authenticate(AuthenticationRequestMessage authRequest)
        {
            _invocationCounter();

            return Task.FromResult(new AuthenticationResponseMessage
            {
                IsAuthenticated = true,
                AuthenticatedIdentity = new RemotingIdentity
                {
                    Name = "fallback-user",
                    IsAuthenticated = true,
                },
            });
        }
    }
}
