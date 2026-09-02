using System;
using System.Linq;
using System.Net.Http;
using System.Security;
using System.Security.Cryptography;
using System.Threading.Tasks;
using CoreRemoting.Toolbox;
using Newtonsoft.Json.Linq;

namespace CoreRemoting.Authentication.Oidc;

/// <summary>
/// Caches the JWKS (JSON Web Key Set) and the discovery document of an identity provider.
/// </summary>
public sealed class JwksCache : IAsyncDisposable, IDisposable
{
    private static readonly TimeSpan UriTtl = TimeSpan.FromHours(24);
    private static readonly TimeSpan KeysTtl = TimeSpan.FromSeconds(60);

    private readonly HttpClient _httpClient;
    private readonly string _issuer;

    private static HttpClient CreateHttpClient(bool acceptSelfSignedCerts)
    {
        var handler = new HttpClientHandler();

#if NET8_0_OR_GREATER
        if (acceptSelfSignedCerts)
        {
            handler.ServerCertificateCustomValidationCallback += (sender, cert, chain, error) => true;
        }
#endif

        return new HttpClient(handler, disposeHandler: true);
    }

    private string _jwksUri;
    private DateTime _jwksUriFetchedAtUtc;

    private JwksKey[] _keys;
    private DateTime _keysFetchedAtUtc;

    /// <summary>
    /// Initializes a new instance of the <see cref="JwksCache"/> class.
    /// </summary>
    /// <param name="issuer">Issuer URL of the identity provider</param>
    /// <param name="developAcceptSelfSignedCerts">
    /// When set to true, invalid server certificates (e.g., self-signed certs of a LAN identity provider) are accepted
    /// when fetching the discovery document and the JWKS. DEV-ONLY: should be false in production; install the CA
    /// certificate instead. Only affects the <see cref="HttpClient"/> owned by this instance.
    /// </param>
    public JwksCache(string issuer, bool developAcceptSelfSignedCerts = false)
    {
        _issuer = issuer ?? throw new ArgumentNullException(nameof(issuer));
        _httpClient = CreateHttpClient(developAcceptSelfSignedCerts);
    }

    /// <summary>
    /// Gets the JWKS URI from the openid-configuration discovery document.
    /// </summary>
    /// <returns>JWKS URI</returns>
    /// <exception cref="SecurityException">Thrown, if the discovery document couldn't be retrieved or is invalid</exception>
    public async Task<string> GetJwksUriAsync()
    {
        if (_jwksUri != null &&
            DateTime.UtcNow - _jwksUriFetchedAtUtc < UriTtl)
            return _jwksUri;

        var discoveryDocument = await FetchJsonObjectAsync(
                new Uri(_issuer!.TrimEnd('/') + "/.well-known/openid-configuration"))
            .ConfigureAwait(false);

        var jwksUri = (string)discoveryDocument["jwks_uri"];
        if (string.IsNullOrEmpty(jwksUri))
            throw new SecurityException("The openid-configuration discovery document doesn't contain a 'jwks_uri' field.");

        _jwksUri = jwksUri;
        _jwksUriFetchedAtUtc = DateTime.UtcNow;

        return _jwksUri;
    }

    /// <summary>
    /// Gets the RSA public keys from the JWKS.
    /// </summary>
    /// <param name="forceRefresh">When set to true, the JWKS is re-fetched although cached data exists.</param>
    /// <returns>RSA public keys</returns>
    /// <exception cref="SecurityException">Thrown, if the JWKS couldn't be retrieved or doesn't contain any RSA key</exception>
    public async Task<JwksKey[]> GetKeysAsync(bool forceRefresh = false)
    {
        if (!forceRefresh && _keys != null && DateTime.UtcNow - _keysFetchedAtUtc < KeysTtl)
            return _keys;

        var jwksUri = await GetJwksUriAsync().ConfigureAwait(false);

        var keysDocument = await FetchJsonObjectAsync(new Uri(jwksUri)).ConfigureAwait(false);

        if (keysDocument["keys"] is not JArray keys || keys.Count == 0)
            throw new SecurityException($"The JWKS at '{jwksUri}' doesn't contain any 'keys' field.");

        _keys = keys
            .Where(key => (string)key["kty"] == "RSA")
            .Select(key => new JwksKey(
                kid: (string)key["kid"],
                rsaParameters: new RSAParameters
                {
                    Modulus = Base64Url.Decode((string)key["n"]),
                    Exponent = Base64Url.Decode((string)key["e"]),
                }))
            .ToArray();

        if (_keys.Length == 0)
            throw new SecurityException($"The JWKS at '{jwksUri}' doesn't contain any RSA public key.");

        _keysFetchedAtUtc = DateTime.UtcNow;

        return _keys;
    }

    private async Task<JObject> FetchJsonObjectAsync(Uri uri)
    {
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.GetAsync(uri).ConfigureAwait(false);
        }
        catch (HttpRequestException e)
        {
            throw new SecurityException($"The OIDC request to '{uri}' failed: {e.Message}", e);
        }

        if (!response.IsSuccessStatusCode)
            throw new SecurityException(
                $"The OIDC request to '{uri}' failed with status code {(int)response.StatusCode}.");

        var rawDocument = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        try
        {
            return JObject.Parse(rawDocument);
        }
        catch (Newtonsoft.Json.JsonException e)
        {
            throw new SecurityException($"The OIDC document at '{uri}' isn't valid JSON: {e.Message}", e);
        }
    }

    /// <summary>
    /// Disposes the <see cref="HttpClient"/> owned by this instance.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        _httpClient.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Disposes the <see cref="HttpClient"/> owned by this instance.
    /// </summary>
    public void Dispose() => DisposeAsync().JustWait();
}
