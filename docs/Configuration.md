## Configuration

### ServerConfig
Property| Description                                                                                                       |Type|Default value
--------|-------------------------------------------------------------------------------------------------------------------|----|-------------
HostName| Host name or IP address                                                                                           |string|"localhost"
NetworkPort| Network port to listen on                                                                                         |int|9090
MessageEncryption| Specifies if communication should be encrypted on message level<br>_See also: [Message Encryption](Security.md)_  |bool|true 
KeySize| key size for asymmetric encryption _(only relevant, if message encryption is enabled)_                            |int|4096
Serializer| Specifies the serializer to be used                                                                               |ISerializerAdapter|BsonSerializerAdapter
Dependency InjectionContainer| DI container used to resolve services                                                                             |IDependency InjectionContainer|CastleWindsorDependency InjectionContainer
RegisterServicesAction| **Optional:** Action that is called on server startup, to register services in the DI container                   |Action<IDependencyInjectionContainer>|null
SessionRepository| Specifies a repository to hold sessions                                                                           |ISessionRepository|SessionRepository
Channel| A channel to handle network communication                                                                         |IServerChannel|TcpServerChannel
AuthenticationProvider| **Optional:** A provider to handle authentification requests<br>_See also: [Autentication](Security.md)_          |IAuthenticationProvider|null
AuthenticationRequired| Specifies if authentification is required                                                                         |bool|false
UniqueServer InstanceName| **Optional:** Unique name of the server instance                                                                  |string|_Guid generated at runtime_
InactiveSession SweepInterval| Sweep interval for inactive sessions in seconds _(No session sweeping if set to 0)_                               |int|60
MaximumSession InactivityTime| Maximum inactivity time in minutes before a session is swept                                                      |int|30
IsDefault| Specifies if this is the default server instance _(only relevant when using Classic Remoting API of CoreRemoting) |bool|false

***

### ClientConfig
Property|Description|Type|Default value
--------|-----------|----|-------------
UniqueClient InstanceName|**Optional:** Unique name of the instcne instance|string|_Guid generated at runtime_
ConnectionTimeout|Connection timeout in seconds _(0 means infinite)_|int|120
AuthenticationTimeout|Authentication timeout in seconds _(0 means infinite)_|int|30
InvocationTimeout|Invocation timeout in seconds _(0 means infinite)_|int|0
ServerHostName|Host name or IP address of the remote server|string|"localhost"
ServerPort|Network port of the remote server|int|9090
Serializer|Specifies the serializer to be used|ISerializerAdapter|BsonSerializerAdapter
MessageEncryption|Specifies if communication should be encrypted on message level<br>_See also: [Message Encryption](https://github.com/theRainbird/CoreRemoting/wiki/Security#message-encryption)_|bool|true 
KeySize|key size for asymmetric encryption _(only relevant, if message encryption is enabled)_|int|4096
Channel|A channel to handle network communication|IClientChannel|TcpClientChannel
Credentials|**Optional:** Array of credentials of authentication _(depends on the authentication provider used on server side)_<br>_See also: [Autentication](https://github.com/theRainbird/CoreRemoting/wiki/Security#authentication)_|Credential[]|null
KeepSessionAliveInterval|Interval in seconds to keep session alive, even on idle _(session is not kept alive if set to 0)_|int|20
IsDefault|Specifies if this is the default client instance _(only relevant when using Classic Remoting API of CoreRemoting)|bool|false

