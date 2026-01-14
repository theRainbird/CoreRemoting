## What about HTTPS?
Currently there is no SSL/TLS implementation that works with .NET Framework 4.x and .NET Core/.NET 5+ and also running on Windows and Linux the same way. So CoreRemoting doesn't support HTTPS out of the box. Instead of encrypting on transport layer, CoreRemoting has integrated support for encryption on message layer.<br>
Please see next section for details.

If you cannot live without HTTPS, then you can implement custom CoreRemoting communication channels.
Implement [IServerChannel](API-Reference.md) and [IClientChannel](API-Reference.md) to accomplish this _(Have a look at [Configuration](https://github.com/theRainbird/CoreRemoting/wiki/Configuration) to find out how you can tell CoreRemoting to use your custom channels)_.

## Message Encryption
If message encryption is enabled _(default setting)_, the serialized messages are signed and encrypted, before sent over the network.<br>

**RSA** is used for asymmetric encryption, signing and secure key exchange.<br>
**AES** is used for symmetric encryption of the message data.

No certificate files are needed. CoreRemoting uses the BCL Cryto APIs directly.

Message encryption configuration must be set the same on server and client _(e.g. if server has message encryption on and client has not, the client will not be able to establish a connection)_. The same goes for key size. Both client and server create their own key public/private key pair. The keys must have the same key size _(default is 4096)_.

## Authentication
CoreRemoting has a built in extensible authentication system. Implement [IAuthenticationProvider](Security.md) interface to add authentication support to your CoreRemoting server. It has only one method, that takes an [Credential](https://github.com/theRainbird/CoreRemoting/wiki/API-Reference#credential) array, which contains the credentials provided by the calling client, and provides an [RemotingIdentity](https://github.com/theRainbird/CoreRemoting/wiki/API-Reference#remotingidentity) object, if authentication was successful.

The following code shows an example implementation of [IAuthenticationProvider](Security.md) _(In a real application in most cases a database is queried in order to check credentials)_:
```C#
    public class SimpleAuthenticationProvider : CoreRemoting.Authentication.IAuthenticationProvider
    {
        public bool Authenticate(Credential[] credentials, out RemotingIdentity authenticatedIdentity)
        {
            authenticatedIdentity = null;
            
            if (credentials == null)
                return false;

            var userName =
                credentials
                    .Where(c => c.Name.ToLower() == "username")
                    .Select(c => c.Value)
                    .FirstOrDefault();
            
            var password =
                credentials
                    .Where(c => c.Name.ToLower() == "password")
                    .Select(c => c.Value)
                    .FirstOrDefault();

            //TODO: Replace with database or LDAP query in your application
            var isAuthenticated = 
                userName == "admin" && 
                password == "secret";

            if (isAuthenticated)
            {
                authenticatedIdentity =
                    new RemotingIdentity()
                    {
                        Name = userName,
                        IsAuthenticated = true
                    };

                return true;
            }

            return false;
        }
    }
```
The authentication provider must be set to the AuthenticationProvider property of the [ServerConfig](Configuration.md).

```C#
    using var server = new RemotingServer(new ServerConfig()
    {
        HostName = "localhost",
        NetworkPort = 9090,
        AuthenticationRequired = true, // <== Only authenticated clients will be able to establish a connection
        AuthenticationProvider = new SimpleAuthenticationProvider(), // <== Tell the CoreRemoting server to use your custom authentication provider
        RegisterServicesAction = container =>
        {
            ...
        }
    });

    server.Start();         
```
### Predefined Authentication Providers
The following predefined authentication providers are available as nuget packages:

**LinuxPamAuthProvider**
Checks credentials against local Linux users _(uses PAM)_
Credentials: **username**, **password** 
https://www.nuget.org/packages/CoreRemoting.Authentication.LinuxPamAuthProvider/

**WindowsAuthProvider**
Checks credentials against Windows users
Credentials: **username**, **password**, _**domain**_ _(optional)_ 
https://www.nuget.org/packages/CoreRemoting.Authentication.WindowsAuthProvider/

**GenericOsAuthProvider** 
Automatically detects OS _(Windows/Linux)_ and selects the proper authentication provider for authentication of local OS users
Credentials: **username**, **password** 
https://www.nuget.org/packages/CoreRemoting.Authentication.GenericOsAuthProvider/

### Provide Client Credentials
Client credentials must be provided as [Credential](https://github.com/theRainbird/CoreRemoting/wiki/API-Reference#credential) array to the Credentials property of the [ClientConfig](https://github.com/theRainbird/CoreRemoting/wiki/Configuration#clientconfig) object. This must be done **before** the client is connected.

The following code shows how to provide credentials for the SimpleAuthenticationProvider example shown above:
```C#
    using var client = new RemotingClient(new ClientConfig()
    {
        ServerHostName = "localhost",
        ServerPort = 9090,
        Credentials = new []
        {
            new Credential("username", "admin"),
            new Credential("password", "secret")
        }
    });
    
    client.Connect();
``` 