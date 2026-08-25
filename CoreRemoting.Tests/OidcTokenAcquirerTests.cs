using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CoreRemoting.Authentication.Oidc;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CoreRemoting.Tests;

[Collection("CoreRemoting")]
public class OidcTokenAcquirerTests
{
    private static int _nextPort = 9600;

    private static int GetPort()
    {
        return Interlocked.Increment(ref _nextPort);
    }

    /// <summary>
    /// A mock OIDC identity provider that serves the discovery document, an authorization endpoint (that issues a
    /// redirect with a code) and a token endpoint (that exchanges a code for tokens).
    /// </summary>
    private sealed class MockOidcIdentityProvider : IDisposable
    {
        private readonly HttpListener _listener;
        private readonly Thread _thread;

        public MockOidcIdentityProvider(int port)
        {
            Port = port;
            Issuer = $"http://localhost:{port}";
            AuthorizationEndpoint = $"{Issuer}/authorize";
            TokenEndpoint = $"{Issuer}/token";

            _listener = new HttpListener();
            _listener.Prefixes.Add($"{Issuer}/");

            _thread = new Thread(ListenLoop) { IsBackground = true };
        }

        public int Port { get; }
        public string Issuer { get; }
        public string AuthorizationEndpoint { get; }
        public string TokenEndpoint { get; }

        public string IdToken { get; set; } = "id-token-123";
        public string AccessToken { get; set; } = "access-token-456";

        public int DiscoveryRequestCount { get; private set; }
        public int TokenRequestCount { get; private set; }

        public string LastCode { get; private set; }
        public string LastCodeVerifier { get; private set; }

        public MockOidcIdentityProvider Start()
        {
            _listener.Start();
            _thread.Start();
            return this;
        }

        private void ListenLoop()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext context;
                try
                {
                    context = _listener.GetContext();
                }
                catch
                {
                    return; // listener was stopped
                }

                try
                {
                    var method = context.Request.HttpMethod;
                    var path = context.Request.Url.AbsolutePath;

                    if (method == "GET" && path == "/.well-known/openid-configuration")
                    {
                        DiscoveryRequestCount++;
                        RespondJson(context, new JObject
                        {
                            ["issuer"] = Issuer,
                            ["authorization_endpoint"] = AuthorizationEndpoint,
                            ["token_endpoint"] = TokenEndpoint,
                        });
                    }
                    else if (method == "GET" && path == "/authorize")
                    {
                        var query = ParseQuery(context.Request.Url?.Query ?? string.Empty);
                        var redirectUri = query.TryGetValue("redirect_uri", out var r) ? r : string.Empty;
                        var state = query.TryGetValue("state", out var s) ? s : string.Empty;
                        var code = "mock-authorization-code";

                        var location = $"{redirectUri}?code={Uri.EscapeDataString(code)}&state={Uri.EscapeDataString(state)}";
                        context.Response.StatusCode = 302;
                        context.Response.AddHeader("Location", location);
                        context.Response.Close();
                    }
                    else if (method == "POST" && path == "/token")
                    {
                        TokenRequestCount++;
                        using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
                        var body = reader.ReadToEnd();
                        var form = ParseQuery(body);

                        LastCode = form.TryGetValue("code", out var c) ? c : null;
                        LastCodeVerifier = form.TryGetValue("code_verifier", out var v) ? v : null;

                        RespondJson(context, new JObject
                        {
                            ["id_token"] = IdToken,
                            ["access_token"] = AccessToken,
                            ["token_type"] = "Bearer",
                            ["expires_in"] = 3600,
                        });
                    }
                    else
                    {
                        context.Response.StatusCode = 404;
                        context.Response.Close();
                    }
                }
                catch
                {
                    try { context.Response.Close(); } catch { /* ignore */ }
                }
            }
        }

        private static void RespondJson(HttpListenerContext context, JObject json)
        {
            var data = Encoding.UTF8.GetBytes(json.ToString());
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = data.Length;
            context.Response.OutputStream.Write(data, 0, data.Length);
            context.Response.Close();
        }

        public void Dispose()
        {
            try { _listener.Stop(); } catch { /* ignore */ }
            try { _listener.Close(); } catch { /* ignore */ }
        }
    }

    /// <summary>
    /// A requirer that captures the authorization URI and returns a fixed code instead of performing an interactive
    /// browser flow.
    /// </summary>
    private sealed class CapturingAcquirer : OidcTokenAcquirer
    {
        public CapturingAcquirer(OidcClientOptions options) : base(options) { }

        public Uri CapturedAuthorizationUri { get; private set; }
        public string CapturedRedirectUri { get; private set; }
        public string AuthorizationCode { get; set; } = "mock-authorization-code";

        protected override Task<string> RequestAuthorizationCodeAsync(
            Uri authorizationUri, string redirectUri, CancellationToken cancellationToken)
        {
            CapturedAuthorizationUri = authorizationUri;
            CapturedRedirectUri = redirectUri;
            return Task.FromResult(AuthorizationCode);
        }
    }

    /// <summary>
    /// A requirer that uses the default loopback redirect strategy (exercises the real loopback listener). The
    /// BrowserOpener simulates a browser by issuing the authorization request through an HttpClient, which follows
    /// the provider's 302 redirect to the loopback listener.
    /// </summary>
    private sealed class LoopbackAcquirer : OidcTokenAcquirer
    {
        public LoopbackAcquirer(OidcClientOptions options) : base(options) { }
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var trimmed = query.TrimStart('?');
        if (trimmed.Length == 0)
            return result;

        foreach (var pair in trimmed.Split(new[] { "&" }, StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = pair.IndexOf('=');
            var key = Uri.UnescapeDataString(equals >= 0 ? pair.Substring(0, equals) : pair);
            var value = equals >= 0 ? Uri.UnescapeDataString(pair.Substring(equals + 1)) : string.Empty;
            result[key] = value;
        }

        return result;
    }

    private static OidcClientOptions CreateOptions(MockOidcIdentityProvider idp, Action<OidcClientOptions> configure = null)
    {
        var options = new OidcClientOptions
        {
            Issuer = idp.Issuer,
            ClientId = "test-client",
            Scopes = new[] { "openid", "profile" },
        };

        configure?.Invoke(options);
        return options;
    }

    [Fact]
    public void Pkce_code_challenge_is_s256_of_code_verifier()
    {
        var pkce = OidcTokenAcquirer.Pkce.Create();

        Assert.NotEqual(pkce.CodeVerifier, pkce.CodeChallenge);

        using var sha256 = SHA256.Create();
        var expected = Base64Url.Encode(sha256.ComputeHash(Encoding.UTF8.GetBytes(pkce.CodeVerifier)));

        Assert.Equal(expected, pkce.CodeChallenge);
    }

    [Fact]
    public void Pkce_verifier_and_challenge_use_base64url_with_expected_length()
    {
        var pkce = OidcTokenAcquirer.Pkce.Create();

        // 32 random bytes -> 43 base64url characters (RFC 7636 requires 43-128).
        Assert.Equal(43, pkce.CodeVerifier.Length);
        Assert.Equal(43, pkce.CodeChallenge.Length);

        Assert.DoesNotMatch("[^A-Za-z0-9_-]", pkce.CodeVerifier);
        Assert.DoesNotMatch("[^A-Za-z0-9_-]", pkce.CodeChallenge);
    }

    [Fact]
    public async Task GetTokenAsync_builds_authorization_url_with_pkce_and_expected_parameters()
    {
        using var idp = new MockOidcIdentityProvider(GetPort()).Start();
        var requirer = new CapturingAcquirer(CreateOptions(idp));

        await requirer.GetTokenAsync();

        var uri = requirer.CapturedAuthorizationUri;
        Assert.NotNull(uri);

        Assert.Equal(idp.AuthorizationEndpoint, uri.GetLeftPart(UriPartial.Path));

        var query = ParseQuery(uri.Query);
        Assert.Equal("code", query["response_type"]);
        Assert.Equal("test-client", query["client_id"]);
        Assert.Equal("http://127.0.0.1:", query["redirect_uri"].Substring(0, "http://127.0.0.1:".Length));
        Assert.Equal("openid profile", query["scope"]);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.False(string.IsNullOrEmpty(query["code_challenge"]));
        Assert.False(string.IsNullOrEmpty(query["state"]));

        Assert.Equal(query["redirect_uri"], requirer.CapturedRedirectUri);
        Assert.StartsWith("http://127.0.0.1:", requirer.CapturedRedirectUri);
    }

    [Fact]
    public async Task GetTokenAsync_returns_id_token_by_default()
    {
        using var idp = new MockOidcIdentityProvider(GetPort()).Start();
        var requirer = new CapturingAcquirer(CreateOptions(idp));

        var token = await requirer.GetTokenAsync();

        Assert.Equal(idp.IdToken, token);
    }

    [Fact]
    public async Task GetTokenAsync_returns_access_token_when_configured()
    {
        using var idp = new MockOidcIdentityProvider(GetPort()).Start();
        var requirer = new CapturingAcquirer(CreateOptions(idp, o => o.TokenKind = OidcTokenKind.AccessToken));

        var token = await requirer.GetTokenAsync();

        Assert.Equal(idp.AccessToken, token);
    }

    [Fact]
    public async Task GetTokenAsync_exchanges_authorization_code_and_sends_code_verifier()
    {
        using var idp = new MockOidcIdentityProvider(GetPort()).Start();
        var requirer = new CapturingAcquirer(CreateOptions(idp));

        await requirer.GetTokenAsync();

        Assert.Equal(idp.TokenRequestCount, 1);
        Assert.Equal("mock-authorization-code", idp.LastCode);
        Assert.NotNull(idp.LastCodeVerifier);
        Assert.Equal(43, idp.LastCodeVerifier.Length);
    }

    [Fact]
    public async Task GetTokenAsync_fetches_discovery_document_once_and_caches_it()
    {
        using var idp = new MockOidcIdentityProvider(GetPort()).Start();
        var requirer = new CapturingAcquirer(CreateOptions(idp));

        await requirer.GetTokenAsync();
        await requirer.GetTokenAsync();

        Assert.Equal(1, idp.DiscoveryRequestCount);
        Assert.Equal(2, idp.TokenRequestCount);
    }

    [Fact]
    public async Task GetTokenAsync_receives_loopback_redirect_and_validates_state()
    {
        using var idp = new MockOidcIdentityProvider(GetPort()).Start();

        var openerCalled = false;
        var requirer = new LoopbackAcquirer(CreateOptions(idp, o =>
            o.BrowserOpener = authUri =>
            {
                openerCalled = true;
                var query = ParseQuery(authUri.Query);
                var redirectUri = query["redirect_uri"];
                var state = query["state"];
                var loopbackRedirect = $"{redirectUri}?code=mock-authorization-code&state={Uri.EscapeDataString(state)}";

                // Fire-and-forget: like a real browser, the opener returns immediately and the loopback listener
                // processes the incoming redirect asynchronously. The client is intentionally not disposed so the
                // in-flight request isn't canceled.
                var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                _ = http.GetAsync(loopbackRedirect);
            }));

        var token = await requirer.GetTokenAsync();

        Assert.True(openerCalled);
        Assert.Equal(idp.IdToken, token);
        Assert.Equal("mock-authorization-code", idp.LastCode);
    }
}
