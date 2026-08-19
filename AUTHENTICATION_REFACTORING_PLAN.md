# Authentication System Refactoring — Implementation Plan

## Overview of Changes by Phase

| # | Feature | Breaking? | Risk |
|---|---------|-----------|------|
| 1 | Random AES session key (not derived from `SessionId`) | Wire format change — adds encrypted-key field to handshake; old clients can connect in compat mode via config flag | Low |
| 2 | Multi-phase auth interface (`IAuthenticationProvider` refactor) | **Yes** — existing providers must adapt. Adapter bridges legacy single-step API automatically | Medium |
| 3 | Negotiated shared key (SRP produces it natively; OIDC uses random AES) | Wire format change — optional negotiated-key field in auth response | Low-Medium |
| 4 | Session resume with previous `SessionId` | New API surface on `ISessionRepository`, new handshake metadata fields. Old clients unaffected | Medium |
| 5 | Per-session variables (`ConcurrentDictionary`) | No (pure addition) | Very low |
| 6 | Tests covering all phases above | — | — |
| 7 | Full SRP implementation as `SrpAuthenticationProvider` | New project, no breaking changes to core types | Medium-High (crypto correctness critical) |

---

## Backward Compatibility Strategy

CoreRemoting is used worldwide in numerous production projects. **No change may break an existing client talking to an unchanged old server.** The following rules apply across all phases:
### Rule 1 — Wire Protocol: No New Message Types

Multi-phase authentication is implemented using the existing `auth` / `auth_response` wire types only. No new message types are introduced.

The current switch statements on both sides already handle unknown message types via a `default:` branch. Because we reuse existing types, backward compatibility is maintained:
- Old client and server continue to use single-step `auth` → `auth_response`. The `IsCompleted` flag on `AuthenticationResponseMessage` is `true` in legacy flow, so old clients will never enter a multi-phase loop.
- New server can respond with `IsCompleted = false` + `Parameters` for challenge data. Old clients that do not understand the loop will simply wait for completion and eventually time out, which is the expected safe degradation for legacy clients.

### Rule 2 — Interface Changes: Deprecation Path (N+1 Version Strategy)
**Never replace an interface directly.** The current `IAuthenticationProvider` has only one method (`Authenticate(Credential[], out RemotingIdentity)` returning bool). Replacing it with a new multi-phase interface would break every consumer immediately.

The correct deprecation path:
- **Version N (current):** Existing single-step API stays intact, untouched. New types added alongside the old ones — `AuthPhase`, `AuthenticationChallenge`, extended `IAuthenticationProvider` with optional methods that have default implementations via extension methods or abstract base class defaults. The new interface is a *superset*, not a replacement.
- **Version N+1:** Old method marked `[Obsolete("Use GetChallenge/ProcessResponse instead")]`. Default adapter implementation provided in core library so existing providers continue working without changes — they implement the old interface and get auto-wrapped via extension method `AsMultiPhaseProvider()`.
- **Version N+2 (future):** After all known consumers have migrated, remove `[Obsolete]` attribute. The actual removal of the legacy API is a separate major-version bump and not part of this refactoring.

### Rule 3 — Auth Loop: Multi-Phase via IsCompleted + Parameters

The current auth flow sends `auth` → waits for `auth_response`. Multi-phase is realized by reusing the same wire types and looping on the server side:

* `AuthenticationResponseMessage.IsCompleted = false` signals the client to send another `auth` request with additional credentials / response data.
* Challenge data is transported in `AuthenticationResponseMessage.Parameters` and client response in `AuthenticationRequestMessage.Credentials`.

No new wire types are sent. Old clients see `IsCompleted = true` from legacy providers and behave unchanged. New clients with an `IAuthenticator` that checks `IsCompleted` can loop until done.

### Rule 4 — Session Key Derivation: Atomic Rekey at the Auth-Completion Boundary
Phase 3 switches the session's shared secret from the Phase 1 random key to a key negotiated by the authentication protocol. The danger with naive mid-session re-keying is that messages already exchanged would be undecryptable under the new key (and vice versa).

**Implemented mitigation (deviates from the earlier draft):** no handshake flag, and AES is *never* skipped. Both sides switch the shared secret atomically at exactly one deterministic boundary — the **final completed `auth_response` carrying a non-null `NegotiatedSharedKey`**:
- Every message exchanged during authentication (including challenge-response rounds) remains fully encrypted with RSA signatures + per-message IV/AES under the random handshake key. That key is only ever consumed by setup traffic and no longer needed after the switch — so nothing becomes undecryptable: both peers rekey after sending/receiving the same final message, and all subsequent traffic uses the negotiated key symmetrically on both sides.
- Server: `RemotingSession.ProcessAuthenticationRequestMessage` applies the rekey only after a completed, successful authentication (and, in legacy mode, after stripping the field from the wire).
- Client: `RemotingClient.ProcessAuthenticationResponseMessage` applies the same switch inside the `IsCompleted` handling.
- No new handshake metadata is needed — the key itself travels inside the existing `auth_response` message type.

### Rule 5 — New Fields on Existing Types: Opt-in via Optional Members
Adding fields like `RemotingIdentity.Claims` (Phase 7) or a negotiated key hint in `AuthenticationResponseMessage` must use optional members with default values, so old deserializers silently ignore them and new ones consume what they understand. The `[DataMember]` attribute on DataContract types already defaults to opt-in deserialization — but we should explicitly set `IsRequired = false` for clarity.

### Rule 6 — ISessionRepository: Additive Only
Phase 4 adds a method (`GetOrCreateResumeCandidate`) and an optional parameter (custom sessionId in `CreateSession`). The default implementation on the base class provides sensible behavior that creates a fresh session when no resume candidate is found, so old code paths work unchanged.

---

## Updated Risk Matrix with Backward Compatibility Mitigations

| Concern | Original risk level | With mitigation |
|---------|--------------------:|-----------------:|
| Wire format changes in handshake (Phase 1) | Low — compat flag mentioned but not specified how it works end-to-end | **Low** — version-negotiation via metadata; old clients fall back to sessionId-derived key automatically when they cannot parse the new field. No server config change needed for mixed deployments. |
| `IAuthenticationProvider` interface replacement (Phase 2) | Medium — "adapter bridges legacy" but no deprecation timeline | **Medium** — kept as-is, but now with explicit N+1 version strategy and `[Obsolete]` attributes on deprecated members rather than outright removal. Existing providers continue compiling without changes through adapter extension methods in core library. |
| Auth loop change from request/response to challenge-response (Phase 2 + SRP/OIDC) | **High** — old clients would timeout waiting for `auth_response`, new servers send challenges first | **Low** — mitigated by reusing `auth`/`auth_response` with `IsCompleted` flag. Legacy providers return `IsCompleted = true`, new providers can return `IsCompleted = false` with `Parameters`. No new wire types, old clients remain in single-step mode. |
| Session key re-keying (Phase 3) | Low-Medium — "re-key all existing encrypted state" is technically difficult and error-prone | **Low** — AES is never disabled; challenge traffic stays encrypted under the Phase 1 random key. Rekeying happens atomically after the final completed `auth_response` on both sides, so no window exists where peers disagree about the active key (Rule 4). |
| Session resume handshake metadata (Phase 4) | Medium | **Medium** — additive only, old servers ignore unknown fields in handshake metadata. Resume requires new clients on both sides anyway for the feature to work end-to-end. |

---

## Phase 1: Separate Session Key from `SessionId`

Replace the practice of using `Guid.NewGuid()` bytes as AES shared secret. Generate a fresh random key per session via `RandomNumberGenerator.GetBytes(32)` for AES-256. The UUID remains only as an identifier — never used as crypto material again.

**Files:**
- `RemotingSession.cs:68` — generate `_sessionKey = RandomNumberGenerator.GetBytes(32)`, keep `Guid _sessionId` unchanged in purpose (identifier only).
- All places that set shared secret to `SessionId.ToByteArray()` (`RemotingSession.cs`: lines 90–95, 136–139, 309–312, 357–360, 423–426) — switch to `_sessionKey`.
- `RemotingClient.cs:SharedSecret()` (line 439–452) and disconnect path — return received key bytes.

**Wire format change:** Extend the handshake message (`SendCompleteHandshakeMessage` in `RemotingSession.cs`) with an optional encrypted AES session key, delivered via existing RSA channel using `RsaKeyExchange.EncryptSecret`. Old clients that don't understand the new field fall back to deriving from sessionId (configurable compat flag).

---

## Phase 2: Multi-Phase Auth via Existing Messages

Multi-phase authentication is implemented without new wire types. The existing `auth` / `auth_response` messages are reused, with `AuthenticationResponseMessage.IsCompleted` and `AuthenticationResponseMessage.Parameters` carrying challenge state.

**Interface**
`IAuthenticationProvider.Authenticate(AuthenticationRequestMessage request)` stays unchanged. Multi-phase is expressed by provider state:

* `AuthenticationResponseMessage.IsCompleted = false` → auth not finished, client must send another `auth` request.
* `AuthenticationResponseMessage.Parameters` carries protocol-specific challenge data e.g. `SALT`, `SERVER_EPHEMERAL_PUBLIC`.
* `AuthenticationRequestMessage.Credentials` carries client response data for the next step e.g. `CLIENT_EPHEMERAL_PUBLIC`, `CLIENT_SESSION_PROOF`.

**Backward compatibility**
* Legacy single-step providers return `IsCompleted = true` immediately → no loop.
* New providers keep per-session state, e.g. `PendingAuthentications` keyed by `sessionId`, and return `IsCompleted = false` until final verification.
* No new wire types are introduced; old clients never see a loop because legacy providers are used.

**Files**
* `RemotingSession.ProcessAuthenticationRequestMessage()` — calls provider, serializes `AuthenticationResponseMessage` via existing `auth_response` wire type.
* `RemotingClient` — `IAuthenticator` implementations loop while `!authResponse.IsCompleted`, sending new `auth` messages with updated credentials.

---

## Phase 3: Negotiated Shared Key via Auth Provider (implemented)

The authentication protocol itself can produce a shared secret (e.g., SRP's `K`). When negotiation is enabled, that key replaces the Phase 1 random AES key for all traffic after authentication.

**Design decision — key travels with the auth result, not on the interface:**
The negotiated key is *not* exposed on `IAuthenticationProvider`. C# interfaces cannot have optional getters, providers need per-session state anyway, and released clients already ignore unknown wire fields silently. Instead, the key rides in the final authentication result via an optional member (Rule 5) on the existing wire type:

```csharp
// AuthenticationResponseMessage (existing [DataContract] type):
[DataMember(IsRequired = false)]
public byte[] NegotiatedSharedKey { get; set; }
```

- Legacy providers leave it `null` → behavior unchanged.
- Negotiating providers set it on the **final** response (`IsCompleted = true`, `IsAuthenticated = true`).

**Generic core mechanism (provider-agnostic):**
- Server — `RemotingSession.ProcessAuthenticationRequestMessage`: after a completed, successful authentication with a non-null key, rekeys via `_sessionKey = NegotiatedSharedKey`. If the server runs in legacy mode (`ServerConfig.UseLegacySessionKeyDerivation = true`) or without message encryption, the field is nulled before serialization — otherwise the client would rekey while the server kept the random key (inconsistent state, guaranteed decrypt failure).
- Client — `RemotingClient.ProcessAuthenticationResponseMessage`: stores the non-null key as session key under the session lock when handling a completed, successful response.
- The rekey boundary is atomic on both sides (server: after sending the final response; client: upon receiving it) and all challenge-phase messages remain encrypted under the Phase 1 handshake key (Rule 4).

**Backward compatibility:**
- Released clients don't know the field and ignore it (`IsRequired = false`). Consequence: when a negotiating provider is enabled, old clients cannot establish sessions — every client must be upgraded before negotiation is switched on (same upgrade ordering story as Phase 1's random key). Documented opt-in requirement.
- Traditional providers (no negotiation) are unaffected in any configuration — the field stays `null` end to end.
- **Implemented deviation from the earlier plan draft:** no `NegotiatedKeyType` handshake flag, and the old idea of skipping AES during the auth phase was *not* implemented. Encryption remains active for the entire session lifecycle (safer), and no new handshake metadata is required because the key piggybacks on `auth_response`.
- Providers opt in individually so existing deployments stay untouched (e.g., `SrpAuthenticationProvider` constructor parameter `useNegotiatedSessionKey = false` by default). The client optionally verifies the received key against its own locally derived value (defense-in-depth / server authentication — implemented for SRP).

**Files:**
- `AuthenticationResponseMessage.cs` — new optional `NegotiatedSharedKey` member.
- `RemotingSession.cs` — conditional rekey + legacy/no-encryption strip in `ProcessAuthenticationRequestMessage`.
- `RemotingClient.cs` — rekey on final `auth_response` in `ProcessAuthenticationResponseMessage`.
- `SrpAuthenticationProvider.cs` / `SrpAuthenticator.cs` — SRP-specific binding (opt-in parameter, key comparison).

---

## Phase 4: Session Resume / Reconnection

Allow clients to reconnect and resume their previous session by presenting its sessionId during handshake.

**Backward compatibility — Rule 6 applied:**
- Additive only on both interfaces and wire protocol. Old servers ignore unknown fields in handshake metadata; old clients do not send the `ResumeSessionId` field, so they simply get a fresh session (current behavior). Resume requires new code on **both sides**, which is acceptable since it's an opt-in feature gated by `ClientConfig.ResumableSessionId`.
- The default implementation of `GetOrCreateResumeCandidate` returns null when no candidate matches, causing the existing create-session path to run unchanged.

**Files:**
- `ISessionRepository.cs` + `SessionRepository.cs` — add method `RemotingSession? GetOrCreateResumeCandidate(Guid requestedSessionId, byte[] clientPublicKey)` that returns the existing active session if it matches the given public key (prevents hijacking). Also extend `CreateSession` with optional custom sessionId parameter for cases where a resumed identity must be preserved.
- `ClientConfig.cs` — add property `Guid? ResumableSessionId`. When set, client includes it in handshake metadata as "I want to resume session X".
- All server connection handlers (`TcpConnection.cs:72–104`, `WebsocketServerConnection.cs:47–65`, `QuicServerConnection.cs:47–62`) — before calling `CreateSession`, check for resumable sessionId in metadata. If present and matches an active session, call resume path instead of fresh creation.
- Wire protocol: add optional field `ResumeSessionId` to handshake metadata (sent as cookie on WS, bytes on TCP/QUIC).

**Security:** Resume requires same client public key AND valid re-authentication — the old session's transport is preserved but identity must be verified again because state may have changed.

---

## Phase 5: Session Variables

Add per-session key-value storage for application-level data (elevated permissions, role flags, etc.).

**Files:**
- `RemotingSession.cs` — add field `_sessionVariables = new ConcurrentDictionary<string, object?>()` and thread-safe accessors (`SetVariable`, `GetVariable<T>`, `HasVariable`). Also expose a serializable snapshot for persistence across server restarts.
- Wire protocol (optional): extend auth response or introduce a dedicated message to transfer session variables from provider during authentication so they're populated immediately after login. **Rule 5 applied:** any new fields on existing types (`AuthenticationResponseMessage`, `RemotingIdentity`) use `[DataMember(IsRequired = false)]` defaults, so old deserializers silently ignore them and new ones consume what they understand.

---

## Phase 6: Tests & Migration of Existing Providers

Update existing providers using the adapter pattern, write new tests covering all phases above including crypto correctness for SRP and random key generation. (Summarized here; details follow in phase-by-phase sections below.)

### Adapter Pattern for Legacy Providers
- Provided as an **extension method** `AsMultiPhaseProvider()` on any legacy provider instance — no source changes to existing providers required, satisfying Rule 2 deprecation path:
  ```csharp
  // In core library:
  public static IAuthenticationProvider AsMultiPhaseProvider(this IAuthenticationProvider legacy) =>
      new LegacyAuthProviderAdapter(legacy);

  // Existing GenericOsAuthProvider / LinuxPamAuthProvider / WindowsAuthProvider compile unchanged.
  ```
- The adapter maps old `Authenticate(Credential[], out Identity)` to multi-phase: `GetChallenge()` returns a trivial challenge with `ProtocolName = "Legacy"`; `ProcessResponse` calls the wrapped provider's single-step method and translates result to `AuthPhase.Done`.

**Backward compatibility — Rule 2 applied:**
- Existing providers (`GenericOsAuthProvider`, `LinuxPamAuthProvider`, `WindowsAuthProvider`) compile without changes because their original interface remains intact in the codebase. The adapter is provided by core library, not required as a wrapper around each provider's source files. Migration to explicit multi-phase implementation can happen incrementally per-provider over subsequent minor releases if desired — it is optional, not mandatory for this plan.
- `[Obsolete]` attribute placed on legacy single-step members in Version N+1 with migration guidance text pointing developers to the new interface and adapter extension method. Actual removal deferred to a future major version bump (out of scope for this refactoring).

---

## Phase 7: OIDC Support (Patterns A + B)

### Backward compatibility
No new wire types. OIDC flows use existing `auth` / `auth_response` with `IsCompleted` and `Parameters`.

### Pattern A — Token Exchange Flow
- `IOidcAuthenticationProvider : IAuthenticationProvider` validates JWTs from client. First request may return `IsCompleted = false` with hint in `Parameters`, e.g. `oidc_challenge`. Client sends JWT back in next `auth` request via `Credentials` named `oidc_token`. Provider validates signature against JWKS, checks expiry/issuer/audience, populates `RemotingIdentity.Claims`.
- No dedicated wire type; JWT travels in `AuthenticationRequestMessage.Credentials`.

### Pattern B — Adaptive / Step-Up Flow
- After initial token validation, provider may return `IsCompleted = false` with `Parameters` containing `step_up_type`. Client `IAuthenticator` prompts app for additional factor and sends next `auth` request with `Credentials` e.g. `totp_code`. Provider repeats until `IsCompleted = true` or max attempts.

### Configuration
- `ServerConfig.OidcConfiguration : IOidcAuthenticationProvider?` — when set, server accepts both OIDC tokens and traditional credentials (auth chain tries OIDC first). Additive only on ServerConfig (Rule 6 equivalent for config types).
- `ClientConfig.Oidc : OidcClientOptions?` with IssuerUrl/ClientId/RedirectUri for auto token acquisition via delegate pattern.

---

## Phase 8: Full SRP Implementation as AuthenticationProvider

This is the most substantial new feature — a complete, production-ready implementation of Secure Remote Password protocol that integrates into all previously described phases (2 multi-phase interface, 3 negotiated key).

### Background on what SRP provides
SRP-6a lets client and server establish a shared secret without ever transmitting a password. The server stores only `v = g^x mod N` where x is the password-derived exponent — even if the database leaks, attackers can't recover passwords offline (they get v which requires interactive guessing). This solves three of the original GitHub issues simultaneously:
- **Strong key derivation** → SRP's shared secret becomes Phase 3's `NegotiatedSharedKey`, replacing random AES with a cryptographically strong session key.
- **Multi-step auth protocol** → naturally fits Phase 2's challenge/response interface (multiple rounds).
- **No password in transit** → the transport layer encryption from Phase 1 is still useful for privacy but doesn't protect against a compromised server; SRP protects even then.

### Backward compatibility — Rule 3 + Rule 4 applied
SRP uses existing `auth` / `auth_response` wire types only. Challenge data is sent in `AuthenticationResponseMessage.Parameters` with `IsCompleted = false`, client response is sent back in `AuthenticationRequestMessage.Credentials`. No new wire types are introduced.

When negotiation is enabled (opt-in via `SrpAuthenticationProvider` constructor parameter `useNegotiatedSessionKey`, default off):
- The Phase 1 random handshake key is still generated and protects all handshake + challenge traffic — encryption is never skipped.
- After a successful SRP authentication, the final `auth_response` carries SRP's `K` as `NegotiatedSharedKey`, and the core rekeys both sides to `K` at the Rule 4 boundary.
- The client additionally verifies that the received key matches its locally derived `K` before accepting it (defense-in-depth / server authentication).

### New project structure

```
CoreRemoting.Authentication.Srp/
├── CoreRemoting.Authentication.Srp.csproj          (targets .NET Standard 2.0, no external deps)
├── SrpAuthenticationProvider.cs                     (main provider — implements IAuthenticationProvider)
├── SrpServerState.cs                                (per-session server state for multi-round protocol)
├── SrpClientState.cs                                (per-session client state; exposed via wire message or internal helper)
├── CredentialTypes.cs                               (constants: "username", "password", etc.)
└── README.md                                        (brief description of SRP usage — only if user asks for docs)
```

### Dependencies

The project targets .NET Standard 2.0 with **zero external NuGet dependencies** — uses `System.Security.Cryptography` primitives already available in the framework:
- `SHA1` / `HMAC-SHA1` (for SRP's internal hash functions; no crypto weakness here, these are just building blocks for group operations)
- `RandomNumberGenerator.GetBytes()` for ephemeral secrets

If we want to support larger prime groups or faster exponentiation than the default 2048-bit RFC 5054 group, consider adding optional dependency on `System.Numerics.Vectors` — but standard SRP-6a with built-in types is sufficient and avoids any external packages.

### Core implementation details

#### 1. SrpAuthenticationProvider.cs (main file)

```csharp
public class SrpAuthenticationProvider : IAuthenticationProvider, IDisposable
{
    // Protocol configuration — uses RFC 5054 default group (2048-bit) by default; 
    // configurable via constructor for stronger groups if desired.
    
    private readonly ISrpPasswordStore _passwordStore;   // abstraction over user credential storage
    
    public string ProtocolName => "SRP-6a";

    public SrpAuthenticationProvider(ISrpPasswordStore passwordStore, int primeBits = 2048) { ... }

    public AuthenticationChallenge GetChallenge(string sessionId);
        → generates random salt (32 bytes), looks up user's v from store
        → if user not found: returns challenge with dummy data so timing doesn't leak existence
        → stores server ephemeral b and B in per-session state
        → returns challenge containing {salt, N_hex, g_hex, B}

    public AuthPhase ProcessResponse(byte[] responseBytes, out RemotingIdentity identity);
        → deserializes client's A value from bytes
        → computes u = H(A || B), k = H(N || g) [k is SRP-6a multiplier]
        → server derives S without seeing the password:
            S = A * v^u  mod N   [server has A from client and v from DB]
          then K = H(S), M_server = H(H(N) XOR H(g) | A | B | K)
        → verifies client's proof M_client == M_server (both sides send H values to prove possession 
          without revealing S — prevents MITM who can't compute K from only one side's data)
        → if verification fails: returns Challenge again with error code so client knows credentials were wrong
        → on success: sets NegotiatedSharedKey = K, identity populated from username in challenge + roles from store
```

#### 2. ISrpPasswordStore.cs (new interface) — server-side abstraction for looking up user data needed by SRP:

```csharp
public interface ISrpPasswordStore
{
    /// <summary>Look up a user's verifier v and salt.</summary>
    SrpUserRecord? Lookup(string username);
    
    /// <summary>Create new user record (salt + verifier) from plaintext password. Call during registration or first login with temporary credentials.</summary>
    SrpUserRecord CreateFromPassword(string username, string password);

    /// <summary>Persist a newly created or updated user record back to the store.</summary>
    void Save(SrpUserRecord record);
}

public class SrpUserRecord
{
    public string Username { get; }           // canonical identifier (lowercased)
    public byte[] Salt { get; }               // random salt for password hashing
    public byte[] Verifier { get; }          // v = g^x mod N where x = H(salt || H(password))
    public IReadOnlyList<string> Roles { get; }  // optional roles from store (e.g., ["admin"])
}
```

The default implementation `InMemorySrpPasswordStore` stores records in-memory for testing. Server administrators supply their own implementation backed by a database, LDAP, or any other source — this keeps the SRP provider dependency-free and testable.

#### 3. Wire protocol usage

No new message types. SRP uses existing `auth` / `auth_response`:

* Server → Client: `auth_response` with `IsCompleted = false` and `Parameters` containing `SALT` and `SERVER_EPHEMERAL_PUBLIC`.
* Client → Server: next `auth` request with `Credentials` containing `USERNAME`, `CLIENT_EPHEMERAL_PUBLIC`, `CLIENT_SESSION_PROOF`.

The provider keeps per-session state keyed by `sessionId` to correlate steps.

#### 4. How it integrates with other phases

| Phase | Integration point |
|-------|------------------|
| **Phase 1 (random AES key)** — still generated and used for all handshake + challenge traffic (encryption is never skipped). After successful SRP auth, the core switches the session secret to `NegotiatedSharedKey` atomically (Rule 4 boundary). |
| **Phase 2 (multi-phase interface)** — SRP provider uses `IsCompleted` + `Parameters` via `auth` / `auth_response`. First round returns `IsCompleted = false` with `SALT`/`SERVER_EPHEMERAL_PUBLIC` in `Parameters`. Second round processes client `Credentials` and returns `IsCompleted = true` on success. |
| **Phase 3 (negotiated key)** — SRP's shared secret K travels in the final `auth_response` as `NegotiatedSharedKey` when opt-in is enabled; the session rekeys to AES under K after auth completes, while challenge messages stay fully encrypted with the handshake key. |
| **Phase 4 (session resume)** — SRP sessions are fully authenticated by the time Phase 3 succeeds, so resumed-session logic works identically. The server stores v per username; on reconnect, same user+password → same K derivation path. |
| **Phase 5 (session variables)** — after successful auth, provider can populate session variables with roles fetched from `ISrpPasswordStore.Roles`. |

#### 5. Server-side storage of verifier v

The SRP password store abstraction gives administrators flexibility:
- **In-memory default**: for tests and simple deployments, use an internal dictionary keyed by username with salt+verifier pairs stored as bytes.
- **Database-backed**: implement `ISrpPasswordStore` wrapping Entity Framework / Dapper against a custom table with columns `(username PK, salt VARBINARY(32), verifier VARBINARY(64))`. The table schema is decided by the implementing admin — CoreRemoting's SRP provider doesn't care how v is persisted.
- **LDAP/AD-backed**: for enterprises already using AD as identity store, provide an example implementation that hashes against a stored password hash (if available) or uses Kerberos pre-auth flow to derive x without knowing plaintext password.

#### 6. Client-side integration

The `RemotingClient` doesn't need SRP-specific code — it just needs the multi-phase auth loop from Phase 2 plus an optional helper:
- Application provides credentials via a delegate `Func<Credential[]> GetCredentials()` registered on client config (similar to existing credential pattern). The default implementation reads from `ClientConfig.Credentials`. On `"srp_challenge"` wire type, framework asks app for username+password; SRP provider's challenge message carries the protocol parameters.
- Alternatively, provide a convenience class `SrpCredentialProvider` that wraps the standard credential API and automatically constructs the A value + proof on client side using the same RFC 5054 group parameters as the server (must match — configurable via constructor with shared N/g values).

#### 7. Client-side SRP math reference implementation

Small helper class `SrpClient` in a separate internal namespace that:
- Takes challenge `{salt, N_hex, g_hex, B}` from server
- Generates random private ephemeral a (32 bytes) and computes A = g^a mod N
- Computes u = H(A || B), k = H(N || g), x = H(salt || password) (using SRP-6a standard derivation: i=1, X = H(s || H(password)))
- Derives S = (B - k*g^x)^a mod N → K = H(S)
- Computes proof M_client = H(H(N) XOR H(g) | A | B | K)

This helper is **internal** to the SRP project — not part of public API. It's exercised only during authentication and discarded after session establishment (the resulting K becomes Phase 3's `NegotiatedSharedKey`).

#### Tests for SRP provider

| Test scenario | What it verifies |
|---------------|-----------------|
| Successful auth with valid credentials in store | Full round-trip: challenge → response → Done, negotiated key derived correctly on both sides |
| Wrong password | Server returns re-challenge (not `Done`); client doesn't get a usable session key even if server lies and sends fake K |
| Username not found | Timing-constant dummy challenge returned so attacker can't distinguish "unknown user" from "wrong password" via timing side-channel |
| Salt randomness across logins for same user | Two `CreateFromPassword` calls produce different salts (proves salt is freshly generated per registration) |
| Same credentials always derive same verifier | Given identical input, `CreateFromPassword` produces deterministic v — required so server's stored verifier matches client's derived x on login |
| NegotiatedSharedKey equals actual SRP shared secret K | Verify the key used for AES encryption after auth is exactly H(S) computed from protocol math, not random bytes |
| MITM cannot pass through without knowing password | If attacker modifies B in transit (e.g., sets B=0), both client and server derive different S values → proofs don't match → auth fails. This proves SRP's security property against active attackers even with a compromised server. |

#### Implementation order within Phase 8

1. **`SrpPasswordStore.csproj + ISrpPasswordStore, InMemorySrpPasswordStore`** — foundation; no protocol logic yet but lets us write tests for storage layer.
2. **Helper class `SrpMath` (internal)** — pure functions: group operations mod N, hash derivations per SRP-6a spec. No dependencies on auth framework types. Heavily test this in isolation.
3. **`SrpAuthenticationProvider.cs`** — main class using `IsCompleted` + `Parameters` via existing `auth`/`auth_response`, keeping per-session state and exposing negotiated key for Phase 3.
4. **Client helper `SrpCredentialProvider`** — builds `CLIENT_EPHEMERAL_PUBLIC` and `CLIENT_SESSION_PROOF` from password and server challenge in `Parameters`.

---

## Summary: Final File Map Across All Phases# Original GitHub Issue Mapping

| Issue from original report | Phase(s) that address it |
|----------------------------|--------------------------|
| SessionId == shared secret, <128 bits entropy (UUID not recommended as secure token) | **Phase 1** — random AES-256 key per session. **Phase 3/8** — SRP produces cryptographically strong negotiated key. |
| No way to provide custom shared key for a session (supported by some auth protocols) | **Phase 3** — optional `NegotiatedSharedKey` member on `AuthenticationResponseMessage`. **Phase 7+8** — OIDC and SRP both exercise this pathway. |
| Auth provider doesn't support multi-step protocols like SRP or 2FA | **Phase 2** — refactored multi-phase interface. **Phase 7 (Pattern B)** — adaptive step-up auth for OIDC/Keycloak. **Phase 8** — full SRP implementation as a concrete provider. |
| SessionId changes on reconnect after server restart; should support session resume (#162) | **Phase 4** — `GetOrCreateResumeCandidate` + resumable sessionId in handshake metadata, with public-key binding to prevent hijacking. |
| No session variables for storing elevated permissions etc. | **Phase 5** — per-session `ConcurrentDictionary<string, object?>`. SRP provider populates roles from password store into these on login (Phase 8). |


| Component | New files | Modified files (core) | Modified auth projects |
|-----------|----------|----------------------|----------------------|
| Phase 1 — random AES key | `EncryptedSessionKey` wire helper | `RemotingSession.cs`, `AesEncryption.cs`, `RsaKeyExchange.cs`, `RemotingClient.cs` | — |
| Phase 2 — multi-phase interface | Adapter for legacy providers | `IAuthenticationProvider.cs`, `RemotingSession.cs`, `RemotingClient.cs`, `AuthenticationResponseMessage.IsCompleted` + `Parameters` usage | All three existing auth projects (via adapter — minimal change) |
| Phase 3 — negotiated key | — | `AuthenticationResponseMessage.cs` (optional `NegotiatedSharedKey` member), `RemotingSession.cs` (rekey + legacy strip in `ProcessAuthenticationRequestMessage`), `RemotingClient.cs` (rekey on final `auth_response`) | `SrpAuthenticationProvider.cs` / `SrpAuthenticator.cs` (opt-in + key verification) |
| Phase 4 — session resume | — | `ISessionRepository.cs`, `SessionRepository.cs`, all server connection handlers for TCP/WS/QUIC/NamedPipe | — |
| Phase 5 — session variables | — | `RemotingSession.cs` (add ConcurrentDictionary + accessors) | — |
| Phase 6 — tests & migration | New test files covering each phase's behavior, including crypto correctness | Existing auth provider projects via adapter pattern updates | All three existing auth providers migrated to multi-phase interface using legacy-adapter bridge |
| **Phase 7 — OIDC** | `CoreRemoting.Authentication.Oidc/IOidcAuthenticationProvider.cs`, `OidcAuthenticationProvider.cs` (JWKS validation, introspection) | `ServerConfig.cs`, `ClientConfig.cs`, `AuthenticationResponseMessage.IsCompleted` + `Parameters` usage, `RemotingIdentity.Claims` | — |
| **Phase 8 — SRP** | New project `CoreRemoting.Authentication.Srp/`: `SrpAuthenticationProvider.cs`, `ISrpPasswordStore.cs`, `InMemorySrpPasswordStore.cs`, internal helper classes | `AuthenticationResponseMessage.IsCompleted` + `Parameters` usage in `RemotingSession`/`RemotingClient`. No new wire types. | — |

---

## Dependency Order & Risk Summary

```
Phase 1 (random key) ────────► Phase 3 (custom negotiated key for SRP/2FA)
       │                                 ▲
       ▼                                 │
Phase 5 (session variables, low risk — can be done anytime)    ← independent

Phase 4 (session resume) ◄── depends on Phases 1+2 (stable session identity + auth flow)

Phases 7a-7d (OIDC Pattern A: token exchange) ──► depends only on Phase 2 interface
Phases 7e-7h (OIDC Pattern B: adaptive/step-up) ──► depends on 7a-7d + Phase 3 negotiated key

Phase 6 (tests covering all phases above) — runs continuously alongside each phase
```

### Risk by component

| Concern | Mitigation |
|---------|-----------|
| Crypto correctness for SRP | Pure-function `SrpMath` helper, heavily tested in isolation against known-answer test vectors from RFC 5054 before integrating into the provider. Code review by someone with crypto experience strongly recommended before shipping to production. |
| Wire format backward compatibility during Phase 1 rollout | Configurable compat flag on server and client: `UseLegacySessionKeyDerivation = true` keeps old behavior; new default uses random key. Both sides must agree for interop, but mixed deployments work until all clients are upgraded. |
| OIDC provider timing attacks / JWKS cache poisoning | Cache keys with strict TTL (5 min), validate issuer on every call, reject tokens with unknown `kid`. Use `System.IdentityModel.Tokens.Jwt` library's built-in validation rather than hand-rolling token parsing. |
| Session hijacking during resume in Phase 4 | Require matching client public key AND re-authentication; never allow transport-level session transfer without identity verification on the new connection. |

---
