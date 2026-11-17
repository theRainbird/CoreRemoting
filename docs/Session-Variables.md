## Session Variables

In addition to session management (see [Sessions](Sessions.md)), CoreRemoting lets an application store arbitrary
per-application data on the server side of a connection. These are called **session variables**. They live for
the lifetime of the session and are accessible from service code running on the server, and from authentication
providers during login.

Session variables are stored in a thread-safe dictionary that belongs to the [RemotingSession](https://github.com/theRainbird/CoreRemoting/wiki/API-Reference#remotingsession).
They are useful for carrying application-level context that is derived from authentication — for example an
elevated permission flag, a role indicator, or a tenant identifier — so that service methods can read it without
re-parsing credentials or re-running authentication logic.

### The ambient session

On the server, the session that is currently processing a call is available through the ambient property
`RemotingSession.Current`. It is `null` outside of a request context (for example, in background code that is not
tied to a client session).

```C#
using CoreRemoting;

public class MyService : ISampleService
{
    public string SayHello()
    {
        var session = RemotingSession.Current;
        if (session == null)
            return "No session";

        // Read a variable that was set during authentication or a previous call.
        bool elevated = session.GetVariable<bool>("Elevated");

        // Store application-level data for the duration of this session.
        session.SetVariable("AccessCount", (session.GetVariable<int>("AccessCount") + 1));

        return elevated ? "Hello, privileged user" : "Hello";
    }
}
```

`RemotingSession.Current` is resolved from an ambient async context that is set while a message is processed on
the server, so service code always sees the session of the client that made the call.

### API

All methods are members of `RemotingSession`.

| Member | Description |
| --- | --- |
| `RemotingSession.Current` | Gets the session currently processing the call (server side only). `null` outside a request context. |
| `SetVariable(string name, object value)` | Sets a variable. Passing `null` as the value **removes** the variable. A `null` name throws `ArgumentNullException`. |
| `GetVariable<T>(string name)` | Gets the variable typed as `T`. Returns `default(T)` if the variable does not exist. Throws `InvalidCastException` if an existing variable has an incompatible type. A `null` name throws `ArgumentNullException`. |
| `TryGetVariable<T>(string name, out T value)` | Safely gets the variable typed as `T`. Returns `false` (and `value` as `default`) if the variable is missing or has an incompatible type. A `null` name throws `ArgumentNullException`. |
| `HasVariable(string name)` | Returns `true` if a variable with the given name exists. A `null` name returns `false`. |
| `RemoveVariable(string name)` | Removes a variable. Returns `true` if it existed. A `null` name throws `ArgumentNullException`. |
| `ClearVariables()` | Removes all session variables. |
| `Variables` | Gets a point-in-time snapshot of all variables as an `IReadOnlyDictionary<string, object>`. The returned dictionary is a copy; mutating it does not affect the session. |
| `SessionId` | Gets this session's unique identifier (`Guid`). |

### Typical use cases

**1. An authentication provider populates variables at login.**

Authentication providers run on the server during the login handshake and can populate session variables that
become readable from service code after the client is logged in.

```C#
public class MyAuthProvider : IAuthenticationProvider
{
    public Task<AuthenticationResponseMessage> Authenticate(AuthenticationRequestMessage authRequest)
    {
        // Runs during login, on the server.
        RemotingSession.Current?.SetVariable("Elevated", isAdmin: true);

        return Task.FromResult(new AuthenticationResponseMessage
        {
            IsCompleted = true,
            IsAuthenticated = true,
            AuthenticatedIdentity = new RemotingIdentity { Name = "bozo", IsAuthenticated = true },
        });
    }
}
```

After login, service code can read the variable:

```C#
bool elevated = RemotingSession.Current?.GetVariable<bool>("Elevated") ?? false;
```

**2. Service code maintains per-session state.**

Service methods can read and update variables on the ambient session. Because the same session processes calls
from the same client, the state persists across calls until the session ends.

### Persistence across parking and resume

When a client's transport is lost abruptly (for example, a network failure without a proper disconnect), the
server **parks** the session instead of removing it. All session state — including session variables — is
preserved. When the same client reconnects, the session is **resumed**: it keeps its session ID and its
variables, and the client authenticates again.

This means session variables survive an unexpected disconnect and are available again after resume, as long as
the session has not timed out or been explicitly removed.

### Thread safety

Session variables are backed by a concurrent dictionary. Reads and writes are safe to perform concurrently from
multiple threads within the same session, so service code can update variables without adding its own locking.

### Client side

The client can read its own session identifier through `client.SessionId`. Session variables themselves are a
server-side concept and are not directly readable from the client; use a service method to read them.
