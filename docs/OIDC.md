## OpenID Connect (OIDC) Authentication

CoreRemoting can authenticate clients against an external **OpenID Connect** identity provider (IdP) such as
[Keycloak](https://www.keycloak.org/). The client performs the standard **Authorization Code flow with PKCE**
(RFC 9192), obtains an OIDC token, and sends it to the CoreRemoting server as an authentication credential.
The server validates the token (a signed JWT) against the identity provider's keys.

This page covers the client-side token acquisition, the server-side token validation, and the multi-phase
**step-up** authentication pattern. See the [Security](Security.md) page for the general authentication model.

### Architecture

The feature is split into a client-side and a server-side part:

| Role | Component | Namespace |
| --- | --- | --- |
| Client | `OidcTokenAcquirer` (abstract) – performs the Authorization Code flow with PKCE and returns the token. | `CoreRemoting.Authentication.Oidc` |
| Client | `OidcAuthenticator` (`IAuthenticator`) – acquires the token and submits it to the server; handles step-up. | `CoreRemoting.Authentication.Oidc` |
| Server | `OidcAuthenticationProvider` (`IOidcAuthenticationProvider`) – validates the JWT against the JWKS. | `CoreRemoting.Authentication.Oidc` |

The token that is sent and validated is the **`id_token`** by default. It carries the standard claims
(`sub`, `iss`, `aud`, `exp`, `nbf`) that the server checks.

### Client side: acquire a token

`OidcTokenAcquirer` is an abstract base class. You derive from it and configure it with an
`OidcClientOptions` instance. The flow works as follows:

1. Fetch and cache the discovery document from `{Issuer}/.well-known/openid-configuration`.
2. Generate a PKCE code verifier and its `S256` challenge.
3. Open the identity provider's authorization page in the browser.
4. The provider redirects back to a redirect URI (a loopback port by default) with an authorization code.
5. Exchange the code (plus the code verifier) at the token endpoint and return the `id_token`.

```C#
using CoreRemoting.Authentication.Oidc;

var options = new OidcClientOptions
{
    Issuer = "https://keycloak.example.com:8443/realms/myrealm",
    ClientId = "coreremoting-client",
    // ClientSecret is optional; leave it null for a public client (Authorization Code flow + PKCE).
    ClientSecret = null,
    // RedirectUri is optional; when null, a loopback URI (http://127.0.0.1:<port>/) is used automatically.
    // Scopes is optional; defaults to ["openid", "profile"]. The "openid" scope is required for an id_token.
    Scopes = new[] { "openid", "profile" },
};

var tokenAcquirer = new MyTokenAcquirer(options);
```

The default interactive strategy uses a **loopback redirect URI**. This is the recommended approach for native
and desktop applications because it does not require registering a custom URI scheme with the operating system.
The listener binds to a free loopback port, the browser is opened automatically, and the returned `state`
parameter is validated to prevent CSRF. To use a different interactive strategy (e.g. an external browser with
a custom redirect URI), override `RequestAuthorizationCodeAsync`.

### Wiring the authenticator

`OidcAuthenticator` accepts the token acquirer directly. On the client, create a proxy only after the client
is connected; authentication happens on the first remote call.

```C#
using CoreRemoting;
using CoreRemoting.Authentication.Oidc;

var client = new RemotingClient(new ClientConfig
{
    ServerHostName = "localhost",
    ServerPort = 9292,
    MessageEncryption = true,
    Authenticator = new OidcAuthenticator(tokenAcquirer),
});

client.Connect();

// The first remote call triggers the OIDC login flow.
ISampleService proxy = client.CreateProxy<ISampleService>();
proxy.SayHello();
```

The `OidcAuthenticator` also has overloads that take a plain `Func<Task<string>>` token provider and an optional
step-up prompt delegate, so you can plug in custom token sources without deriving from `OidcTokenAcquirer`.

### OidcClientOptions

| Property | Type | Default | Description |
| --- | --- | --- | --- |
| `Issuer` | `string` | – (required) | Issuer URL of the IdP. Must match the `iss` claim of the token. The discovery document is fetched from `{Issuer}/.well-known/openid-configuration`. |
| `ClientId` | `string` | – (required) | OAuth client id. |
| `ClientSecret` | `string?` | `null` | Optional client secret. Leave `null` for a public client. |
| `RedirectUri` | `string?` | `null` | Redirect URI. When `null`, a loopback URI (`http://127.0.0.1:<port>/`) is used. |
| `Scopes` | `string[]` | `["openid", "profile"]` | OIDC scopes to request. `openid` is required to obtain an `id_token`. |
| `TokenKind` | `OidcTokenKind` | `IdToken` | Which token of the token response to return (`IdToken` or `AccessToken`). |
| `HttpClient` | `HttpClient?` | `null` | Optional reusable `HttpClient`. When `null`, one is created internally. |
| `BrowserOpener` | `Action<Uri>?` | `null` | Optional delegate to open the browser. When `null`, an OS-specific default opener is used (falling back to printing the URL). |
| `AuthorizationTimeout` | `TimeSpan` | 5 minutes | Maximum time to wait for the authorization redirect. |
| `DevelopAcceptSelfSignedCerts` | `bool` | `false` | DEV-ONLY: accept invalid/self-signed certificates when fetching the discovery document. Should be `false` in production; install the CA certificate instead. Only affects the internally created `HttpClient`. |

### Server side: validate a token

On the server, configure an `OidcAuthenticationProvider`. It validates the received JWT against the identity
provider's public keys, which it fetches from the JWKS referenced in the discovery document. The JWKS and
discovery document are cached (`JwksCache`).

```C#
using CoreRemoting;
using CoreRemoting.Authentication.Oidc;

var server = new RemotingServer(new ServerConfig
{
    HostName = "localhost",
    NetworkPort = 9292,
    AuthenticationRequired = true,
    AuthenticationProvider = new OidcAuthenticationProvider(new OidcOptions
    {
        Issuer = "https://keycloak.example.com:8443/realms/myrealm",
        AllowedAudiences = new[] { "coreremoting-client" },
    }),
    RegisterServicesAction = container =>
        container.RegisterService<ISampleService, SampleService>(),
});

server.Start();
```

`OidcOptions`

| Property | Type | Default | Description |
| --- | --- | --- | --- |
| `Issuer` | `string` | – (required) | Issuer URL of the IdP. Must match the `iss` claim of incoming tokens. |
| `AllowedAudiences` | `string[]` | – (required) | Accepted `aud` claims. A token is valid if its `aud` contains at least one of these. |
| `ClockSkew` | `TimeSpan` | 1 minute | Tolerated skew when validating `exp` and `nbf` claims. |
| `AllowInsecureIssuer` | `bool` | `false` | Whether an issuer over `http` is allowed. When `false`, the issuer must use `https`. |
| `RoleClaimName` | `string` | `"roles"` | Claim that holds the identity's roles (multiple values are used). |
| `StepUpValidator` | `Func<string, string, bool>?` | `null` | Optional delegate for step-up validation (see below). |
| `NegotiateNewSessionKey` | `bool` | `false` | Whether to negotiate a new random session key during authentication. |
| `DevelopAcceptSelfSignedCerts` | `bool` | `false` | DEV-ONLY: accept invalid/self-signed certificates when fetching the discovery document and JWKS. Should be `false` in production. |

The provider validates the `sub`, `iss`, `aud`, `exp`, and `nbf` claims and, when present, maps the
`RoleClaimName` claim to the authenticated identity's roles.

### Step-up authentication (Pattern B)

Authentication can be a **multi-phase** process. After the initial token is validated, the server may request an
additional factor within the same session. The client is then prompted for a code (e.g. a one-time password) and
submits it.

To use step-up, provide a `StepUpValidator` on the server and a step-up prompt delegate on the client:

```C#
// Server: validate the step-up code (here: a simple static check as an example).
new OidcOptions
{
    Issuer = issuer,
    AllowedAudiences = new[] { clientId },
    StepUpValidator = (identityName, code) => code == "123456",
}
```

```C#
// Client: prompt the user for the code when the server requests a step-up.
var authenticator = new OidcAuthenticator(
    tokenAcquirer,
    stepUpPrompt: async stepUpType =>
    {
        Console.Write($"Enter the {stepUpType} code: ");
        return Console.ReadLine();
    });
```

### Running the example

The [LoginUsingKeycloakDemo](https://github.com/theRainbird/CoreRemoting/tree/master/Examples/LoginUsingKeycloakDemo)
example demonstrates a complete client/server setup against a Keycloak realm. All configuration is read from
environment variables so that no hostnames are hardcoded:

| Environment variable | Required | Description |
| --- | --- | --- |
| `KEYCLOAK_ISSUER` | yes | Issuer URL, e.g. `https://keycloak.example.com:8443/realms/myrealm`. Must exactly match the token's `iss` claim (Realm settings → Endpoints → Issuer). |
| `KEYCLOAK_CLIENT_ID` | yes | OAuth client id. Also configured as the server's allowed audience. |
| `KEYCLOAK_CLIENT_SECRET` | no | Client secret. Omit for a public client. |
| `KEYCLOAK_REDIRECT_URI` | no | Redirect URI. Empty uses the loopback redirect. |
| `KEYCLOAK_SCOPES` | no | Comma-separated scopes. Defaults to `openid,profile`. |
| `KEYCLOAK_ACCEPT_SELF_SIGNED_CERTS` | no | Dev-only flag. When `true`, self-signed certificates of a LAN identity provider are accepted. See below. |

A ready-to-copy template is available in the example folder as `.env.example`.

### Self-signed certificates on the LAN

A LAN-hosted identity provider such as Keycloak commonly uses a self-signed certificate. Two independent HTTPS
connections are involved:

- The **client** discovers the IdP via `OidcTokenAcquirer`, which honors `DevelopAcceptSelfSignedCerts`.
- The **server** fetches the JWKS via `JwksCache`, which honors `OidcOptions.DevelopAcceptSelfSignedCerts`.

Both flags default to `false`. For development you can set them (via the `KEYCLOAK_ACCEPT_SELF_SIGNED_CERTS`
environment variable in the example). **In production, do not enable these flags** — instead install the CA
certificate that signed the provider's certificate on both the client and the server machine.

> Note: `DevelopAcceptSelfSignedCerts` is a dev-only convenience. Disabling certificate validation weakens the
> security of the connection and must never be used in production.
