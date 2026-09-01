using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace CoreRemoting.Authentication.Oidc;

/// <summary>
/// Client-side OIDC token requirer.
/// Performs the OpenID Connect Authorization Code flow with PKCE (RFC 9192) against an identity provider
/// (e.g., Keycloak), opens the provider's login page and returns the resulting OIDC token.
/// </summary>
/// <remarks>
/// The default interactive strategy uses a loopback redirect URI (http://127.0.0.1:<port>/), which is the
/// recommended approach for native/desktop applications. Subclasses can override
/// <see cref="RequestAuthorizationCodeAsync"/> to implement a different interactive strategy.
/// </remarks>
public abstract class OidcTokenAcquirer
{
    private readonly OidcClientOptions _options;
    private readonly HttpClient _httpClient;
    private readonly object _discoveryLock = new();
    private JObject _cachedDiscovery;
    private string _currentAuthorizationState;

    /// <summary>
    /// Initializes a new instance of the <see cref="OidcTokenAcquirer"/> class.
    /// </summary>
    /// <param name="options">OIDC client options</param>
    protected OidcTokenAcquirer(OidcClientOptions options)
    {
        if (options == null)
            throw new ArgumentNullException(nameof(options));

        if (string.IsNullOrWhiteSpace(options.Issuer))
            throw new ArgumentException("OidcClientOptions.Issuer must not be empty.", nameof(options));

        if (string.IsNullOrWhiteSpace(options.ClientId))
            throw new ArgumentException("OidcClientOptions.ClientId must not be empty.", nameof(options));

        _options = options;

        if (options.HttpClient != null)
        {
            _httpClient = options.HttpClient;
        }
        else
        {
            var handler = new HttpClientHandler();

#if NET8_0_OR_GREATER
            if (options.DevelopAcceptSelfSignedCerts)
            {
                handler.ServerCertificateCustomValidationCallback += (sender, cert, chain, error) => true;
            }
#endif

            _httpClient = new HttpClient(handler, disposeHandler: true);
        }

        BrowserOpener = options.BrowserOpener ?? DefaultBrowserOpener;
    }

    /// <summary>
    /// Gets the options this requirer was created with.
    /// </summary>
    protected OidcClientOptions Options => _options;

    /// <summary>
    /// Gets the delegate used to open the browser for the authorization request.
    /// </summary>
    protected Action<Uri> BrowserOpener { get; }

    /// <summary>
    /// Performs the Authorization Code flow with PKCE and returns the resulting OIDC token.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The OIDC token (id_token or access_token)</returns>
    public async Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        var discovery = await GetDiscoveryDocumentAsync(cancellationToken).ConfigureAwait(false);

        var pkce = Pkce.Create();
        _currentAuthorizationState = GenerateRandomToken(32);

        var redirectUri = ResolveRedirectUri();
        var authorizationUri = BuildAuthorizationUrl(discovery, pkce, redirectUri, _currentAuthorizationState);

        var authorizationCode =
            await RequestAuthorizationCodeAsync(authorizationUri, redirectUri, cancellationToken)
                .ConfigureAwait(false);

        return await ExchangeCodeAsync(discovery, pkce, redirectUri, authorizationCode, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Performs the interactive part of the flow: opens the authorization page and returns the authorization code
    /// that the identity provider redirects back to the redirect URI. The default implementation uses a loopback
    /// redirect URI.
    /// </summary>
    /// <param name="authorizationUri">Fully built authorization URI (includes PKCE challenge, state and redirect URI)</param>
    /// <param name="redirectUri">Redirect URI the code is redirected back to</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The authorization code</returns>
    protected virtual Task<string> RequestAuthorizationCodeAsync(
        Uri authorizationUri, string redirectUri, CancellationToken cancellationToken)
        => RunLoopbackRedirectAsync(authorizationUri, redirectUri, cancellationToken);

    private async Task<string> RunLoopbackRedirectAsync(
        Uri authorizationUri, string redirectUri, CancellationToken cancellationToken)
    {
        using var listener = new HttpListener();
        listener.Prefixes.Add(redirectUri);
        listener.Start();

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var receiveTask = listener.GetContextAsync();

        BrowserOpener(authorizationUri);

        Task completedTask = await Task.WhenAny(
            receiveTask, Task.Delay(_options.AuthorizationTimeout, linkedCts.Token))
            .ConfigureAwait(false);

        if (completedTask != receiveTask)
        {
            if (cancellationToken.IsCancellationRequested)
                throw new OperationCanceledException(cancellationToken);

            throw new TimeoutException(
                $"The authorization redirect wasn't received within " +
                $"{_options.AuthorizationTimeout.TotalSeconds:0} seconds.");
        }

        HttpListenerContext context;
        try
        {
            context = await receiveTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }

        try
        {
            var values = ParseQuery(context.Request.Url?.Query ?? string.Empty);

            if (values.TryGetValue("error", out var error))
                throw new InvalidOperationException(
                    $"The identity provider rejected the authorization request: {error}.");

            if (!values.TryGetValue("code", out var code) || string.IsNullOrEmpty(code))
                throw new InvalidOperationException(
                    "The identity provider didn't return an authorization code in the redirect.");

            var returnedState = values.TryGetValue("state", out var state) ? state : null;
            if (!string.Equals(returnedState, _currentAuthorizationState, StringComparison.Ordinal))
                throw new InvalidOperationException("The 'state' value of the authorization redirect doesn't match.");

            var buffer = Encoding.UTF8.GetBytes(
                "<html><body><h2>Authentication successful</h2>" +
                "<p>You can close this window and return to the application.</p></body></html>");

            context.Response.StatusCode = 200;
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.ContentLength64 = buffer.Length;
            context.Response.OutputStream.Write(buffer, 0, buffer.Length);
            context.Response.OutputStream.Close();

            return code;
        }
        finally
        {
            try { listener.Stop(); } catch { /* listener teardown errors are not relevant to the caller */ }
            try { listener.Close(); } catch { /* listener teardown errors are not relevant to the caller */ }
        }
    }

    private async Task<JObject> GetDiscoveryDocumentAsync(CancellationToken cancellationToken)
    {
        lock (_discoveryLock)
        {
            if (_cachedDiscovery != null)
                return _cachedDiscovery;
        }

        var discoveryUri = new Uri(_options.Issuer.TrimEnd('/') + "/.well-known/openid-configuration");

        JObject document;
        try
        {
            using var response = await _httpClient.GetAsync(discoveryUri, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            document = JObject.Parse(json);
        }
        catch (HttpRequestException e)
        {
            throw new InvalidOperationException(
                $"The OIDC discovery document at '{discoveryUri}' couldn't be retrieved: {e.Message}", e);
        }

        if ((string)document["issuer"] == null ||
            (string)document["authorization_endpoint"] == null ||
            (string)document["token_endpoint"] == null)
        {
            throw new InvalidOperationException(
                "The OIDC discovery document is missing required fields " +
                "(issuer, authorization_endpoint, token_endpoint).");
        }

        lock (_discoveryLock)
        {
            if (_cachedDiscovery == null)
                _cachedDiscovery = document;
        }

        return document;
    }

    private string ResolveRedirectUri()
    {
        if (!string.IsNullOrWhiteSpace(_options.RedirectUri))
            return _options.RedirectUri;

        return $"http://127.0.0.1:{GetFreeTcpPort()}/";
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private Uri BuildAuthorizationUrl(JObject discovery, Pkce pkce, string redirectUri, string state)
    {
        var authorizationEndpoint = (string)discovery["authorization_endpoint"];
        if (string.IsNullOrEmpty(authorizationEndpoint))
            throw new InvalidOperationException(
                "The OIDC discovery document doesn't contain an authorization_endpoint.");

        var parameters = new List<KeyValuePair<string, string>>
        {
            new("response_type", "code"),
            new("client_id", _options.ClientId),
            new("redirect_uri", redirectUri),
            new("scope", string.Join(" ", _options.Scopes)),
            new("code_challenge", pkce.CodeChallenge),
            new("code_challenge_method", "S256"),
            new("state", state),
        };

        var query = string.Join(
            "&",
            parameters.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));

        return new Uri(authorizationEndpoint + "?" + query);
    }

    private async Task<string> ExchangeCodeAsync(
        JObject discovery, Pkce pkce, string redirectUri, string code, CancellationToken cancellationToken)
    {
        var tokenEndpoint = (string)discovery["token_endpoint"];
        if (string.IsNullOrEmpty(tokenEndpoint))
            throw new InvalidOperationException(
                "The OIDC discovery document doesn't contain a token_endpoint.");

        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["client_id"] = _options.ClientId,
            ["code_verifier"] = pkce.CodeVerifier,
        };

        if (!string.IsNullOrWhiteSpace(_options.ClientSecret))
            form["client_secret"] = _options.ClientSecret;

        using var content = new FormUrlEncodedContent(form);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsync(tokenEndpoint, content, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException e)
        {
            throw new InvalidOperationException(
                $"The OIDC token request to '{tokenEndpoint}' failed: {e.Message}", e);
        }

        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var tokenResponse = JObject.Parse(json);

        if (_options.TokenKind == OidcTokenKind.AccessToken)
        {
            var accessToken = (string)tokenResponse["access_token"];
            if (string.IsNullOrEmpty(accessToken))
                throw new InvalidOperationException(
                    "The OIDC token response doesn't contain an 'access_token'.");

            return accessToken;
        }

        var idToken = (string)tokenResponse["id_token"];
        if (string.IsNullOrEmpty(idToken))
            throw new InvalidOperationException(
                "The OIDC token response doesn't contain an 'id_token'. " +
                "Make sure the 'openid' scope is requested.");

        return idToken;
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

    private static string GenerateRandomToken(int byteLength)
    {
        var bytes = new byte[byteLength];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);

        return Base64Url.Encode(bytes);
    }

    private static void DefaultBrowserOpener(Uri uri)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                Process.Start("xdg-open", uri.AbsoluteUri);
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                Process.Start("open", uri.AbsoluteUri);
            else
                Console.WriteLine($"Please open manually: {uri}");
        }
        catch
        {
            Console.WriteLine($"Please open manually: {uri}");
        }
    }

    /// <summary>
    /// Implements PKCE (code verifier + code challenge) as described in RFC 7636.
    /// </summary>
    internal sealed class Pkce
    {
        private Pkce(string codeVerifier, string codeChallenge)
        {
            CodeVerifier = codeVerifier;
            CodeChallenge = codeChallenge;
        }

        /// <summary>
        /// Gets the code verifier (43-128 unreserved characters).
        /// </summary>
        public string CodeVerifier { get; }

        /// <summary>
        /// Gets the S256 code challenge.
        /// </summary>
        public string CodeChallenge { get; }

        /// <summary>
        /// Creates a new PKCE pair.
        /// </summary>
        /// <returns>PKCE pair</returns>
        public static Pkce Create()
        {
            var codeVerifier = GenerateRandomToken(32);

            string codeChallenge;
            using (var sha256 = SHA256.Create())
            {
                var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(codeVerifier));
                codeChallenge = Base64Url.Encode(hash);
            }

            return new Pkce(codeVerifier, codeChallenge);
        }
    }
}
