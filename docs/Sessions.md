## Sessions

CoreRemoting has an integrated session management. A session is created when a client is successfully connected to the server.<br> 
If authentication is configured and the [AuthenticationRequired](https://github.com/theRainbird/CoreRemoting/wiki/API-Reference#authenticationrequired-1) property is set to true, a session is only created, if the credentials are correct _(See [Security](https://github.com/theRainbird/CoreRemoting/wiki/Security#authentication) chapter for more information about authentication)_.

### Session Repository
A CoreRemoting server instance holds a session repository to manage all sessions. A session repository is a class that implements the [ISessionRepository](https://github.com/theRainbird/CoreRemoting/wiki/API-Reference#isessionrepository) interface. If no session repository is configured explicitly, a default session repository, which stores the session data in memory, is used.

### Session Lifetime
By default sessions are removed automatically, after 30 minutes of inactivity. This limit can be changed on server configuration _(See [Configuration](https://github.com/theRainbird/CoreRemoting/wiki/Configuration) chapter for details)_.
Every call that is made on a session resets this inactivity counter.
Client instances have a timer running, that are sending a heartbeat (ping) to the server in a defined interval, to keep the session alive, as long as the client is connected. By default the interval is set to 20 seconds. You can change this by setting the KeepSessionAliveInterval property of the [ClientConfig](https://github.com/theRainbird/CoreRemoting/wiki/Configuration#clientconfig). If the interval is set to 0, the timer is stopped and so the session is not kept alive automatically.

### Reconnection and session resume

A client can transparently recover from a lost connection — for example after a network interruption or a server restart — without the application code having to intervene. This is built on three cooperating mechanisms:

1. **Heartbeat (ping/pong) for proactive loss detection.** The keep-alive timer described above no longer just sends empty bytes: it sends a `ping` message and expects a `pong` back. If a previously sent ping is still unanswered when the next tick fires, the connection is considered lost. This lets the client notice a dead connection on its own, instead of only discovering it on the next remote call. Detection takes two consecutive missed pongs, so the worst-case detection time is roughly `2 × KeepSessionAliveInterval` (with the default interval of 20 seconds, about 40 seconds). Tune `KeepSessionAliveInterval` to match how quickly you need to notice a lost connection.

2. **Session resume.** When a connection is lost, the server *parks* the session instead of destroying it. A reconnecting client asks the server to resume the parked session by presenting its session ID and a signature over that ID, signed with the same key material it used originally (hijack protection — see `ResumableSessionId` and `SessionSignature` on the client). If the keys match, the very same session (including its session variables) is reattached to the new transport.

3. **Subscription recovery.** Event subscriptions (`+=` on a proxied service event) are remembered on the client in a replay registry. After a reconnect the client compares its subscription checksum with the one the server reports for the resumed session. If they differ — which is always the case when the session was resumed on a *different* server instance, whose event hubs start out empty — the client automatically re-subscribes all of its event handlers. This means events raised by the new server instance reach the client's handlers again, without the application re-wiring anything.

#### Enabling automatic reconnect

Automatic reconnection is opt-in. Configure it on the [ClientConfig](https://github.com/theRainbird/CoreRemoting/wiki/Configuration#clientconfig):

| Property | Default | Meaning |
| --- | --- | --- |
| `AutoReconnect` | `false` | When `true`, the client automatically tries to re-establish the connection after an unexpected loss. |
| `ReconnectMinDelay` | `1` | Initial delay (in seconds) before the first reconnect attempt. |
| `ReconnectMaxDelay` | `30` | Upper bound (in seconds) for the exponentially growing retry delay. |

A **manual** `Disconnect()` / `DisconnectAsync()` always suppresses auto-reconnect; only an *unexpected* loss (heartbeat timeout or a dropped transport) triggers the retry loop. The loop stops as soon as a connect succeeds, or when the client is disposed.

#### Resume-refused fallback

If the client asks to resume a specific session but the server cannot honor it (the session is gone, expired, or the signature doesn't match), the behaviour is controlled by the **server-side** `ServerConfig.AllowSessionResumeFallback`. The server decides this policy and communicates it to the client in the handshake:

- `false` (default): the connect fails with a `RemotingException` ("Server refused to resume the requested session …"). This preserves the original, strict behaviour, so existing applications that relied on the exception are unaffected.
- `true`: the client continues with a freshly created session and re-subscribes its handlers, so the connection stays usable instead of failing.

The flag lives on the server (not the client) because whether a refused resume may be silently downgraded to a new session is a server policy decision. When a new client talks to an older server that does not send the flag, the client reads it as `false` and keeps the strict behaviour.

#### Reconnection events

The client exposes events so the application can observe the connection lifecycle:

- `AfterConnect` — raised after every successful connect (initial or reconnect).
- `Reconnected` — raised only when reconnecting after a loss. The `ReconnectedEventArgs` carry `SessionResumed` (whether the original session was resumed vs. a new one created), `SessionId`, and `SubscriptionsRestored` (whether the subscription replay verified cleanly).
- `SubscriptionsDesyncDetected` — raised if, after the replay, the client's and the server's subscription checksums still disagree. The `SubscriptionDesyncEventArgs` carry both checksums. This signals a condition the automatic recovery could not resolve and is also reported through the client's error handler.

### Persistence extension point

By default the session repository keeps sessions in memory, so a parked session survives a client reconnect but **not** a full process restart of the server. To survive a server process restart, implement `ISessionRepository` with a durable backing store (disk, database, etc.).

The resumable identity of a session is small and stable: the **session ID** and the **client's public key** (the client's private key blob is what lets it prove ownership of that identity on reconnect). A durable repository only needs to persist enough state to answer `TryResumeSession` after a restart:

- `CreateSession` should persist the session's ID and the client public key alongside whatever session variables the application needs.
- `TryResumeSession` looks the session up by ID and verifies the presented signature against the stored client public key, then reattaches the new transport.
- `RemoveSession` should delete the persisted record.

The library deliberately does **not** ship a built-in serializer for session state: what is worth persisting is application-specific (session variables, in-flight work, etc.), and the security-sensitive parts (key material) must be handled with care. Implementing `ISessionRepository` against your storage of choice is the supported extension point. When a new server instance starts with a durable repository that still knows a parked session, a reconnecting client resumes exactly as described above — the new server's empty event hubs are repopulated by the client's subscription replay.

