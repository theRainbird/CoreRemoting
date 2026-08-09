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

### Rule 1 — Wire Protocol: Opt-in New Message Types, Graceful Ignoring
The current switch statements on both sides (`RemotingSession.OnReceiveMessage` line 275–289 and `RemotingClient.OnMessage` line 526–550) already handle unknown message types via a `default:` branch that either logs an error (server side) or silently ignores (client side). **New wire types must be designed so that old endpoints can safely ignore them.**

Specifically:
- Old client receiving new server messages (`"auth_challenge"`, `"srp_challenge"`, etc.): currently the default case on `RemotingClient.OnMessage` line 546–549 silently ignores unknown types. This is acceptable — the auth loop will time out, which is correct behavior for an old client that cannot speak multi-phase protocols.
- Old server receiving new client messages: the switch at `RemotingSession.OnReceiveMessage` line 275–289 logs `"Invalid message type X"` and discards it. This would cause authentication to fail with a timeout on the client side, which is also acceptable — old servers cannot speak multi-phase protocols either.

### Rule 2 — Interface Changes: Deprecation Path (N+1 Version Strategy)
**Never replace an interface directly.** The current `IAuthenticationProvider` has only one method (`Authenticate(Credential[], out RemotingIdentity)` returning bool). Replacing it with a new multi-phase interface would break every consumer immediately.

The correct deprecation path:
- **Version N (current):** Existing single-step API stays intact, untouched. New types added alongside the old ones — `AuthPhase`, `AuthenticationChallenge`, extended `IAuthenticationProvider` with optional methods that have default implementations via extension methods or abstract base class defaults. The new interface is a *superset*, not a replacement.
- **Version N+1:** Old method marked `[Obsolete("Use GetChallenge/ProcessResponse instead")]`. Default adapter implementation provided in core library so existing providers continue working without changes — they implement the old interface and get auto-wrapped via extension method `AsMultiPhaseProvider()`.
- **Version N+2 (future):** After all known consumers have migrated, remove `[Obsolete]` attribute. The actual removal of the legacy API is a separate major-version bump and not part of this refactoring.

### Rule 3 — Auth Loop: Negotiation via Wire Protocol Version Flag
The current auth flow (`RemotingClient.AuthenticateAsync`) sends credentials in one request → waits for `auth_response`. **A new multi-phase server sending `"srp_challenge"` or `"auth_challenge"` before the client has sent anything would cause a timeout** because the old client's `_authenticationCompletedTaskSource.Task` is only signaled by `ProcessAuthenticationResponseMessage`, never by challenge messages.

Mitigation: Add a protocol version negotiation in handshake metadata (extension of existing `complete_handshake`). Both sides advertise their supported auth-protocol versions upfront, so multi-phase servers know to use legacy single-step flow for old clients and challenge-response for new ones. The server inspects client capabilities during the initial connection setup before sending any auth-related wire types.

### Rule 4 — Session Key Derivation: Never Re-key Mid-session
Phase 3 proposes replacing `_sessionKey` with `NegotiatedSharedKey` after authentication succeeds. If messages were already exchanged (e.g., during an SRP challenge-response loop) using the random key, **those messages become undecryptable** once we swap to the negotiated key — and vice versa.

Mitigation: When a provider returns a non-null `NegotiatedSharedKey`, do NOT generate `_sessionKey` at all in Phase 1. Instead, skip AES encryption entirely during the challenge-response phase (messages are signed but not encrypted with per-message IV), then swap to the negotiated key only after auth completes. The handshake message format must include a flag indicating "negotiated-key mode" so both sides know whether to expect AES-encrypted messages or unencrypted-wrapped-by-RSA-only messages during auth.

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
| Auth loop change from request/response to challenge-response (Phase 2 + SRP/OIDC) | **High** — old clients would timeout waiting for `auth_response`, new servers send challenges first | **Medium** — mitigated by protocol version negotiation during handshake; server uses legacy single-step flow when client advertises old auth-version. New wire types only sent to capable clients. |
| Session key re-keying mid-session (Phase 3) | Low-Medium — "re-key all existing encrypted state" is technically difficult and error-prone | **Low** — mitigated by skipping AES entirely during challenge-response when negotiated key available; no mid-session swap needed. Handshake flag communicates mode to client upfront. |
| Session resume handshake metadata (Phase 4) | Medium | **Medium** — additive only, old servers ignore unknown fields in handshake metadata. Resume requires new clients on both sides anyway for the feature to work end-to-end. |
| New message types ignored by old endpoints | Low | **Low** — documented behavior: old client/server ignores unknown wire type via default case; results in expected timeout or discarded-message, no crash. |

### Timeline Recommendation (Semantic Versioning)

```
Current version: X.Y.Z  ← stable release with all existing code unchanged

Next minor release: X.(Y+1).0
├── Phase 5 (session variables): additive only, safe to ship in minor release
├── Wire protocol extensions are opt-in and backward-compatible by design
└── All new features require explicit configuration — old clients/servers work as before

Major bump on next version: (X+1).0.0
├── Phase 1 (random AES key): changes default behavior; requires both sides to upgrade for full benefit
│   └── Old client → new server: works with fallback derivation from sessionId
│   └── New client → old server: works because old server still sends sessionId in handshake bytes
├── Phase 2 (multi-phase interface): deprecated legacy members marked [Obsolete], adapter provided
├── Phase 4 (session resume): additive API, no breaking changes to existing contract
├── Phase 7 (OIDC) and Phase 8 (SRP): new optional providers — zero impact on existing code paths

Future major: (X+2).0.0 (not part of this plan)
└── Remove [Obsolete] legacy members from IAuthenticationProvider after ecosystem migration complete
```

---

## Phase 1: Separate Session Key from `SessionId`

Replace the practice of using `Guid.NewGuid()` bytes as AES shared secret. Generate a fresh random key per session via `RandomNumberGenerator.GetBytes(32)` for AES-256. The UUID remains only as an identifier — never used as crypto material again.

**Files:**
- `RemotingSession.cs:68` — generate `_sessionKey = RandomNumberGenerator.GetBytes(32)`, keep `Guid _sessionId` unchanged in purpose (identifier only).
- All places that set shared secret to `SessionId.ToByteArray()` (`RemotingSession.cs`: lines 90–95, 136–139, 309–312, 357–360, 423–426) — switch to `_sessionKey`.
- `RemotingClient.cs:SharedSecret()` (line 439–452) and disconnect path — return received key bytes.

**Wire format change:** Extend the handshake message (`SendCompleteHandshakeMessage` in `RemotingSession.cs`) with an optional encrypted AES session key, delivered via existing RSA channel using `RsaKeyExchange.EncryptSecret`. Old clients that don't understand the new field fall back to deriving from sessionId (configurable compat flag).

---

## Phase 2: Multi-Phase Auth Interface Refactor

Replace single-step synchronous `Authenticate()` with stateful multi-phase interface. This is the foundation on which SRP, OIDC adaptive flows, and legacy provider adapters all build.

**New types:**
```csharp
public enum AuthPhase { Challenge, Responding, Done }

public class AuthenticationChallenge
{
    public byte[]? Data;                    // protocol-specific challenge bytes (e.g., salt + A for SRP)
    public string ProtocolName;             // e.g. "SRP-6a", "OIDC"
    public IReadOnlyDictionary<string, string>? Metadata;  // optional hints for client UI
}

public interface IAuthenticationProvider
{
    /// <summary>Protocol name this provider implements (e.g., "LocalPassword", "SRP-6a").</summary>
    string ProtocolName { get; }

    /// <summary>Initial challenge — called once per session to kick off auth.</summary>
    AuthenticationChallenge GetChallenge(string sessionId);

    /// <summary>Process client's response. Returns the next phase (Done, Responding for more rounds, or Challenge again if re-challenged).</summary>
    AuthPhase ProcessResponse(byte[] responseBytes, out RemotingIdentity identity);
}
```

**Backward compatibility — Rule 2 + Rule 3 applied:**
- `IAuthenticationProvider` is **NOT replaced**. The existing single-step API stays intact in the codebase. New types (`AuthPhase`, `AuthenticationChallenge`) and new methods are added alongside via an abstract base class or extension method that provides a default adapter: any provider implementing only the old `Authenticate(Credential[], out RemotingIdentity)` signature gets auto-wrapped into the multi-phase interface without source changes. Marked `[Obsolete]` with migration guidance in Version N+1 (see Backward Compatibility Strategy timeline).
- **Protocol version negotiation**: during handshake, server inspects client's advertised auth-version and uses legacy single-step flow (`"auth"` → `"auth_response"`) for old clients or multi-phase challenge-response loop (new wire types) only when the client signals capability. This prevents timeouts on old clients that cannot handle new message types.
- Wire protocol: add two message types `AuthenticationChallengeMessage` (server→client) and `AuthenticationStepResponseMessage` (client→server). New wire type strings `"auth_challenge"` / `"auth_step_response"`. Old endpoints ignore these via their existing default cases — no crash, just expected timeout for legacy clients.
- `RemotingSession.ProcessAuthenticationRequestMessage()` — replace direct `_server.Authenticate()` call with multi-phase loop: get challenge → send to client → receive response → process until `Done` or failure (only when protocol version negotiated as capable).
- `RemotingClient.AuthenticateAsync()` — handle the new challenge-response loop; when server sends `"auth_challenge"`, wait for app-provided credentials and send back, repeating as needed. Falls through to legacy single-step flow if negotiation indicates old client/server pair.

---

## Phase 3: Negotiated Shared Key via Auth Provider

Add optional property on auth provider that, when set after successful protocol negotiation, replaces the random AES key with a shared secret derived by the authentication protocol itself (e.g., SRP).

```csharp
public interface IAuthenticationProvider
{
    // ... existing members from Phase 2 + legacy single-step API preserved in parallel ...

    /// <summary>Shared cryptographic material produced during auth. When non-null, used as AES session key instead of random.</summary>
    byte[]? NegotiatedSharedKey { get; }
}
```

**Backward compatibility — Rule 4 applied:**
- **Do NOT generate `_sessionKey` at all when negotiated-key mode is active.** SRP and similar protocols produce the key themselves during auth, so generating a random one first then swapping it mid-session (as originally proposed) would corrupt any messages already exchanged with AES. Instead: handshake metadata includes `NegotiatedKeyType = true/false`. When `true`, server skips `_sessionKey` generation entirely; challenge-response messages use RSA signature only (no per-message IV/AES). After auth completes, both sides switch to AES using the negotiated key for all subsequent traffic.
- The legacy single-step flow (Phase 1 compat mode) continues to work unchanged: no provider returns a negotiated key in that path, so `_sessionKey` is generated as before and used throughout the session lifecycle.

**Files:**
- `IAuthenticationProvider.cs` — add the property above (extended from Phase 2). Legacy single-step API preserved via adapter for N+1 deprecation.
- Handshake wire format — extend with `NegotiatedKeyType: bool` flag in metadata so client knows whether to expect AES or RSA-only during auth phase. Both sides must agree; old servers default to false (random key mode), which is the current behavior.
- `RemotingSession` handshake completion path — when negotiated-key mode active, swap from no-AES to AES with NegotiatedSharedKey after auth completes. No mid-session re-keying needed because nothing was encrypted before this point.

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

### Backward compatibility — Rule 3 applied
- New wire types `"auth_oidc_token"` / `"auth_step_up_request"` are only sent after protocol-version negotiation confirms the client/server pair supports multi-phase auth. Old clients/servers that advertise legacy version continue to use single-step flow and never see these new message types.

### Pattern A — Token Exchange Flow
- New `IOidcAuthenticationProvider : IAuthenticationProvider` with constructor taking IssuerUrl, ClientId, Audience. Implements `GetChallenge()` to return OIDC config hint and `ProcessResponse(JWT)` to validate signature against JWKS from discovery endpoint (cached 5 min), check expiry/issuer/audience claims → returns identity populated from token claims + new `RemotingIdentity.Claims` dictionary field for raw JWT data (`[DataMember(IsRequired = false)]`, Rule 5).
- Wire: new message type `"auth_oidc_token"` carrying the bearer JWT.

### Pattern B — Adaptive / Step-Up Flow
- When Keycloak adaptive policies require additional verification, server sends step-up challenge (`"auth_step_up_request"` wire type) with `ChallengeType` (TotpCode, ReAuthenticate, CustomPrompt). Client framework exposes hook event for app to collect response and send back. Provider calls introspection endpoint after initial token validation; if more auth needed → re-challenge client until Done or max attempts exceeded.

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
SRP uses new wire types (`"srp_challenge"` / `"srp_response"`) and negotiated-key mode, both gated by protocol-version negotiation during handshake (Rule 3). Old clients/servers that advertise legacy auth version never see SRP messages. When negotiated-key mode is active:
- Server skips `_sessionKey` generation entirely in Phase 1 — no AES key exists yet because the real key comes from SRP math itself.
- Challenge-response wire types carry only RSA signature (no per-message IV/AES), so there is **nothing to corrupt** if we later swap to AES with the negotiated key after auth completes. No mid-session re-keying needed.

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

#### 3. Wire protocol additions (new message types)

| Message | Direction | Purpose |
|---------|-----------|---------|
| `SrpChallengeMessage` (extends `AuthenticationChallengeMessage`) | Server → Client | Carries `{salt, N_hex, g_hex, B}` — first SRP round challenge from server. Sent via wire type `"srp_challenge"`. |
| `SrpClientResponseMessage` (extends `AuthenticationStepResponseMessage`) | Client → Server | Carries `{A, M_client_proof}` — client's ephemeral public value and proof of password knowledge. Wire type `"srp_response"`. |

The existing multi-phase auth loop from Phase 2 naturally dispatches these based on the challenge's protocol name (`ProtocolName = "SRP-6a"`). The `RemotingSession` switch in its message handler recognizes SRP-specific wire types and routes them to the appropriate provider.

#### 4. How it integrates with other phases

| Phase | Integration point |
|-------|------------------|
| **Phase 1 (random AES key)** — skipped when using SRP, because Phase 3's negotiated key replaces it entirely. The handshake still sends `B` encrypted via RSA during the initial phase; only after successful auth does the server swap `_sessionKey = NegotiatedSharedKey`. |
| **Phase 2 (multi-phase interface)** — SRP provider implements this directly: `GetChallenge()` → first round, returns Challenge with salt+B. `ProcessResponse(A_bytes)` → second/third round, verifies client proof and sets Done if valid. |
| **Phase 3 (negotiated key)** — SRP's shared secret K is exactly what Phase 3 expects as `NegotiatedSharedKey`. The session re-keys immediately after auth completes; any messages already exchanged in plaintext during challenge/response can be considered ephemeral (they contain no useful data beyond protocol parameters). |
| **Phase 4 (session resume)** — SRP sessions are fully authenticated by the time Phase 3 succeeds, so resumed-session logic works identically. The server stores v per username; on reconnect, same user+password → same K derivation path. |
| **Phase 5 (session variables)** — after successful auth, provider can populate session variables with roles fetched from `ISrpPasswordStore.Roles` or additional claims the store returns in a new method signature like `Roles { get; }`. |

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
3. **Wire message classes** (`SrpChallengeMessage`, `SrpClientResponseMessage`) extending existing Phase 2 base messages.
4. **`SrpAuthenticationProvider.cs`** — main class tying store + math + wire protocol together, implementing the multi-phase interface from Phase 2 and exposing negotiated key for Phase 3.
5. **Wire type routing in `RemotingSession`** — teach existing message dispatch to recognize `"srp_challenge"` / `"srp_response"`.

---

## Summary: Final File Map Across All Phases

| Component | New files | Modified files (core) | Modified auth projects |
|-----------|----------|----------------------|----------------------|
| Phase 1 — random AES key | `EncryptedSessionKey` wire helper | `RemotingSession.cs`, `AesEncryption.cs`, `RsaKeyExchange.cs`, `RemotingClient.cs` | — |
| Phase 2 — multi-phase interface | New base message classes, adapter for legacy providers | `IAuthenticationProvider.cs`, `RemotingSession.cs:350-398`, `RemotingClient.cs:462-503`, all channel connection handlers (`TcpConnection.cs`, etc.) | All three existing auth projects (via adapter — minimal change) |
| Phase 3 — negotiated key | — | `IAuthenticationProvider.cs` extended, `RemotingSession.cs` handshake completion path | — |
| Phase 4 — session resume | New wire type fields in handshake metadata | `ISessionRepository.cs`, `SessionRepository.cs`, all server connection handlers for TCP/WS/QUIC/NamedPipe | — |
| Phase 5 — session variables | — | `RemotingSession.cs` (add ConcurrentDictionary + accessors) | — |
| Phase 6 — tests & migration | New test files covering each phase's behavior, including crypto correctness | Existing auth provider projects via adapter pattern updates | All three existing auth providers migrated to multi-phase interface using legacy-adapter bridge |
| **Phase 7 — OIDC** | `CoreRemoting.Authentication.Oidc/IOidcAuthenticationProvider.cs`, `OidcAuthenticationProvider.cs` (JWKS validation, introspection), `SrpCredentialProvider` client helper, new wire types `"auth_oidc_token"`, `"auth_step_up_request"` | `ServerConfig.cs`, `ClientConfig.cs`, existing auth dispatch in `RemotingSession`, new field on `RemotingIdentity` for raw JWT claims dict | — |
| **Phase 8 — SRP** | New project `CoreRemoting.Authentication.Srp/`: `SrpAuthenticationProvider.cs`, `ISrpPasswordStore.cs`, `InMemorySrpPasswordStore.cs`, internal helper classes, wire message extensions (`SrpChallengeMessage`, `SrpClientResponseMessage`) | Wire type dispatch in `RemotingSession` to recognize `"srp_challenge"` / `"srp_response"`. Minor update to client-side auth loop config for SRP credential source. | — |

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

## Original GitHub Issue Mapping

| Issue from original report | Phase(s) that address it |
|----------------------------|--------------------------|
| SessionId == shared secret, <128 bits entropy (UUID not recommended as secure token) | **Phase 1** — random AES-256 key per session. **Phase 3/8** — SRP produces cryptographically strong negotiated key. |
| No way to provide custom shared key for a session (supported by some auth protocols) | **Phase 3** — `NegotiatedSharedKey` on provider interface. **Phase 7+8** — OIDC and SRP both exercise this pathway. |
| Auth provider doesn't support multi-step protocols like SRP or 2FA | **Phase 2** — refactored multi-phase interface. **Phase 7 (Pattern B)** — adaptive step-up auth for OIDC/Keycloak. **Phase 8** — full SRP implementation as a concrete provider. |
| SessionId changes on reconnect after server restart; should support session resume (#162) | **Phase 4** — `GetOrCreateResumeCandidate` + resumable sessionId in handshake metadata, with public-key binding to prevent hijacking. |
| No session variables for storing elevated permissions etc. | **Phase 5** — per-session `ConcurrentDictionary<string, object?>`. SRP provider populates roles from password store into these on login (Phase 8). |
