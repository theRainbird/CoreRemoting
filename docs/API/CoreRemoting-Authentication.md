# CoreRemoting.Authentication Namespace API Reference

This namespace contains the authentication framework for CoreRemoting, providing secure client authentication and identity management capabilities.

## Core Interfaces

### 🔄 IAuthenticationProvider
**Namespace:** `CoreRemoting.Authentication`

Interface for authentication providers. Defines the contract for validating client credentials and establishing authenticated identities.

#### Key Methods

| Method | Return Type | Description |
|--------|-------------|-------------|
| `Authenticate(Credential[] credentials, out RemotingIdentity authenticatedIdentity)` | `bool` | Authenticates provided credentials and returns authenticated identity if successful |

#### Parameters

| Parameter | Type | Description |
|-----------|------|-------------|
| `credentials` | `Credential[]` | Array of credentials to authenticate |
| `authenticatedIdentity` | `out RemotingIdentity` | Output parameter for authenticated identity |

#### Returns

Returns `true` if authentication is successful, `false` otherwise.

#### Usage Examples

```csharp
public class DatabaseAuthProvider : IAuthenticationProvider
{
    public bool Authenticate(Credential[] credentials, out RemotingIdentity authenticatedIdentity)
    {
        authenticatedIdentity = null;
        
        if (credentials == null || credentials.Length == 0)
            return false;
            
        var username = credentials.FirstOrDefault(c => c.Name == "username")?.Value;
        var password = credentials.FirstOrDefault(c => c.Name == "password")?.Value;
        
        if (ValidateUser(username, password))
        {
            authenticatedIdentity = new RemotingIdentity
            {
                Name = username,
                IsAuthenticated = true,
                AuthenticationType = "Database",
                Roles = GetUserRoles(username)
            };
            return true;
        }
        
        return false;
    }
    
    private bool ValidateUser(string username, string password) { /* ... */ }
    private string[] GetUserRoles(string username) { /* ... */ }
}
```

```csharp
// Using custom authentication provider
var config = new ServerConfig()
{
    AuthenticationRequired = true,
    AuthenticationProvider = new DatabaseAuthProvider()
};

var server = new RemotingServer(config);
```

---

## Data Classes

### 🏗️ Credential
**Namespace:** `CoreRemoting.Authentication`  
**Attributes:** `[Serializable]`

Describes an authentication credential containing a name-value pair for authentication data.

#### Key Properties

| Property | Type | Description |
|----------|------|-------------|
| `Name` | `string` | Name of the credential (e.g., "username", "password", "token") |
| `Value` | `string` | Value of the credential (e.g., "john", "secret123", "abc123def") |

#### Usage Examples

```csharp
// Creating credentials
var credentials = new[]
{
    new Credential { Name = "username", Value = "john.doe" },
    new Credential { Name = "password", Value = "secretpassword" },
    new Credential { Name = "domain", Value = "company.com" }
};

// Client configuration with credentials
var clientConfig = new ClientConfig()
{
    ServerHostName = "localhost",
    ServerPort = 9090,
    Credentials = credentials
};

var client = new RemotingClient(clientConfig);
```

```csharp
// API key authentication
var apiKeyCredentials = new[]
{
    new Credential { Name = "api_key", Value = "sk-1234567890abcdef" }
};
```

---

### 🏗️ Credential
**Namespace:** `CoreRemoting.Authentication`  
**Attributes:** `[Serializable]`

Describes an authentication credential containing a name-value pair for authentication data. This class is used to pass authentication information from client to server.

#### Key Properties

| Property | Type | Description |
|----------|------|-------------|
| `Name` | `string` | Name of the credential (e.g., "username", "password", "token", "api_key") |
| `Value` | `string` | Value of the credential (e.g., "john.doe", "secret123", "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...") |

#### Usage Examples

```csharp
// Creating credentials for username/password authentication
var credentials = new[]
{
    new Credential { Name = "username", Value = "john.doe" },
    new Credential { Name = "password", Value = "secretpassword" },
    new Credential { Name = "domain", Value = "company.com" }
};

// Client configuration with credentials
var clientConfig = new ClientConfig()
{
    ServerHostName = "localhost",
    ServerPort = 9090,
    Credentials = credentials
};

var client = new RemotingClient(clientConfig);
```

```csharp
// API key authentication
var apiKeyCredentials = new[]
{
    new Credential { Name = "api_key", Value = "sk-1234567890abcdef" }
};
```

```csharp
// Multi-factor authentication with custom fields
var mfaCredentials = new[]
{
    new Credential { Name = "username", Value = "user@example.com" },
    new Credential { Name = "password", Value = "strongpassword123" },
    new Credential { Name = "otp_code", Value = "123456" },
    new Credential { Name = "device_id", Value = "device-abc-123" }
};
```

---

### 🏗️ RemotingIdentity
**Namespace:** `CoreRemoting.Authentication`  
**Interfaces:** `IIdentity`  
**Attributes:** `[DataContract]`, `[Serializable]`

Represents an authenticated identity on the server side. Contains user information, roles, and authentication status.

#### Key Properties

| Property | Type | Description |
|----------|------|-------------|
| `Name` | `string` | Name of the authenticated identity (username, user ID, etc.) |
| `Domain` | `string` | Optional domain or realm name of the identity |
| `Roles` | `string[]` | Array of roles that the identity is a member of |
| `AuthenticationType` | `string` | String describing the authentication type (e.g., "Database", "JWT", "LDAP") |
| `IsAuthenticated` | `bool` | Whether the identity was successfully authenticated |

#### Usage Examples

```csharp
// Creating authenticated identity
var identity = new RemotingIdentity
{
    Name = "john.doe",
    Domain = "company.com",
    IsAuthenticated = true,
    AuthenticationType = "ActiveDirectory",
    Roles = new[] { "Users", "Managers", "Admin" }
};

// Using in authentication provider
public bool Authenticate(Credential[] credentials, out RemotingIdentity authenticatedIdentity)
{
    if (ValidateCredentials(credentials))
    {
        authenticatedIdentity = new RemotingIdentity
        {
            Name = ExtractUsername(credentials),
            IsAuthenticated = true,
            AuthenticationType = "CustomAuth",
            Roles = GetUserRoles(ExtractUsername(credentials))
        };
        return true;
    }
    
    authenticatedIdentity = null;
    return false;
}
```

```csharp
// Accessing identity on server side
server.BeforeCall += (sender, context) =>
{
    var session = server.SessionRepository.GetSession(context.SessionId);
    if (session?.Identity != null)
    {
        Console.WriteLine($"User: {session.Identity.Name}");
        Console.WriteLine($"Roles: {string.Join(", ", session.Identity.Roles)}");
        Console.WriteLine($"Auth Type: {session.Identity.AuthenticationType}");
    }
};
```

---

## Message Classes

### 🏗️ AuthenticationRequestMessage
**Namespace:** `CoreRemoting.Authentication`

Message sent from client to server containing authentication credentials for validation.

#### Key Properties

| Property | Type | Description |
|----------|------|-------------|
| `Credentials` | `Credential[]` | Array of credentials to be authenticated |

### 🏗️ AuthenticationResponseMessage
**Namespace:** `CoreRemoting.Authentication`

Message sent from server to client containing the result of authentication attempt.

#### Key Properties

| Property | Type | Description |
|----------|------|-------------|
| `IsAuthenticated` | `bool` | Whether authentication was successful |
| `AuthenticatedIdentity` | `RemotingIdentity` | The authenticated identity (null if authentication failed) |

---

## Authentication Flow

### Complete Authentication Process

1. **Client Configuration**: Client configures credentials in `ClientConfig`
2. **Connection**: Client connects to server with optional credentials
3. **Request**: If credentials are provided, client sends `AuthenticationRequestMessage`
4. **Validation**: Server calls `IAuthenticationProvider.Authenticate()`
5. **Response**: Server sends `AuthenticationResponseMessage` with result
6. **Session**: If successful, `RemotingIdentity` is stored in session

#### Flow Diagram

```
Client                               Server
  |                                    |
  |-- Connect() ---------------------->|
  |                                    |
  |-- AuthenticationRequestMessage --->|
  |                                    |-- IAuthenticationProvider.Authenticate()
  |                                    |-- Validate credentials
  |                                    |
  |<-- AuthenticationResponseMessage --|
  |                                    |
  |                                    |-- Store RemotingIdentity in session
```

---

## Implementation Examples

### Database Authentication Provider

```csharp
public class DatabaseAuthProvider : IAuthenticationProvider
{
    private readonly MyDbContext _dbContext;
    
    public DatabaseAuthProvider(MyDbContext dbContext)
    {
        _dbContext = dbContext;
    }
    
    public bool Authenticate(Credential[] credentials, out RemotingIdentity authenticatedIdentity)
    {
        authenticatedIdentity = null;
        
        if (credentials == null)
            return false;
            
        var username = credentials.FirstOrDefault(c => c.Name == "username")?.Value;
        var password = credentials.FirstOrDefault(c => c.Name == "password")?.Value;
        
        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            return false;
        
        var user = _dbContext.Users
            .Include(u => u.Roles)
            .FirstOrDefault(u => u.Username == username && u.IsActive);
            
        if (user == null || !VerifyPassword(password, user.PasswordHash))
            return false;
        
        authenticatedIdentity = new RemotingIdentity
        {
            Name = user.Username,
            Domain = "MyApp",
            IsAuthenticated = true,
            AuthenticationType = "Database",
            Roles = user.Roles.Select(r => r.Name).ToArray()
        };
        
        // Update last login
        user.LastLoginAt = DateTime.UtcNow;
        _dbContext.SaveChanges();
        
        return true;
    }
    
    private bool VerifyPassword(string password, string hash) { /* ... */ }
}
```

### LDAP Authentication Provider

```csharp
public class LdapAuthProvider : IAuthenticationProvider
{
    private readonly string _ldapServer;
    private readonly string _domain;
    
    public LdapAuthProvider(string ldapServer, string domain)
    {
        _ldapServer = ldapServer;
        _domain = domain;
    }
    
    public bool Authenticate(Credential[] credentials, out RemotingIdentity authenticatedIdentity)
    {
        authenticatedIdentity = null;
        
        var username = credentials?.FirstOrDefault(c => c.Name == "username")?.Value;
        var password = credentials?.FirstOrDefault(c => c.Name == "password")?.Value;
        
        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            return false;
        
        try
        {
            using var connection = new LdapConnection(_ldapServer);
            
            // Bind with user credentials
            var credential = new NetworkCredential(username, password, _domain);
            connection.Credential = credential;
            connection.Bind();
            
            // Get user groups/roles
            var searchRequest = new SearchRequest(
                $"CN={username},OU=Users,DC={_domain}",
                "(objectClass=user)",
                SearchScope.Subtree,
                new[] { "memberOf" });
                
            var searchResponse = (SearchResponse)connection.SendRequest(searchRequest);
            var roles = ExtractRoles(searchResponse);
            
            authenticatedIdentity = new RemotingIdentity
            {
                Name = username,
                Domain = _domain,
                IsAuthenticated = true,
                AuthenticationType = "LDAP",
                Roles = roles
            };
            
            return true;
        }
        catch (Exception ex)
        {
            // Log authentication failure
            return false;
        }
    }
    
    private string[] ExtractRoles(SearchResponse response) { /* ... */ }
}
```
---

## Security Considerations

### Best Practices

1. **Secure Transport**: Always use message encryption (`MessageEncryption = true`)
2. **Credential Validation**: Validate all input parameters and handle null/empty values
3. **Error Handling**: Don't reveal specific authentication failures (user not found vs wrong password)
4. **Rate Limiting**: Implement rate limiting to prevent brute force attacks
5. **Session Management**: Use appropriate session timeouts and cleanup
6. **Password Storage**: Never store plain text passwords, always use salted hashes

---

## See Also

- [CoreRemoting](CoreRemoting.md) - Core client and server classes
- [CoreRemoting.Channels](CoreRemoting-Channels.md) - Transport layer
- [CoreRemoting.RpcMessaging](CoreRemoting-RpcMessaging.md) - Message types
- [Security](../Security.md) - Security overview and best practices