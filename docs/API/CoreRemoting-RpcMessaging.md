# CoreRemoting.RpcMessaging Namespace API Reference

This namespace contains RPC message types and message building framework for CoreRemoting, providing the messaging infrastructure for remote procedure calls.

## Core Interfaces

### 🔄 IMessageEncryptionManager
**Namespace:** `CoreRemoting.RpcMessaging`

Interface for message encryption and decryption managers that handle secure message transmission.

#### Key Methods

| Method | Return Type | Description |
|--------|-------------|-------------|
| `CreateWireMessage(string messageType, ISerializerAdapter serializer, byte[] serializedMessage, RsaKeyPair keyPair, byte[] sharedSecret, byte[] uniqueCallKey)` | `WireMessage` | Creates encrypted wire message |
| `GetDecryptedMessageData(WireMessage message, ISerializerAdapter serializer, byte[] sharedSecret, byte[] sendersPublicKeyBlob, int sendersPublicKeySize)` | `byte[]` | Decrypts message data from wire message |

#### Usage Examples

```csharp
// Custom encryption manager
public class CustomEncryptionManager : IMessageEncryptionManager
{
    public WireMessage CreateWireMessage(
        string messageType, 
        ISerializerAdapter serializer, 
        byte[] serializedMessage, 
        RsaKeyPair keyPair, 
        byte[] sharedSecret, 
        byte[] uniqueCallKey)
    {
        // Custom encryption logic
        var encryptedData = CustomEncrypt(serializedMessage, sharedSecret);
        
        return new WireMessage
        {
            MessageType = messageType,
            Data = encryptedData,
            UniqueCallKey = uniqueCallKey
        };
    }
    
    public byte[] GetDecryptedMessageData(
        WireMessage message, 
        ISerializerAdapter serializer, 
        byte[] sharedSecret, 
        byte[] sendersPublicKeyBlob, 
        int sendersPublicKeySize)
    {
        // Custom decryption logic
        return CustomDecrypt(message.Data, sharedSecret);
    }
}
```

**Implemented by:** `MessageEncryptionManager`

---

### 🔄 IMethodCallMessageBuilder
**Namespace:** `CoreRemoting.RpcMessaging`

Interface for building method call messages from method metadata and arguments.

#### Key Methods

| Method | Return Type | Description |
|--------|-------------|-------------|
| `BuildMethodCallMessage(MethodInfo method, object[] args, Guid uniqueCallKey, string serviceName)` | `MethodCallMessage` | Builds method call message from method info and arguments |

**Implemented by:** `MethodCallMessageBuilder`

---

## Core Classes

### 🏗️ MessageEncryptionManager
**Namespace:** `CoreRemoting.RpcMessaging`  
**Interfaces:** `IMessageEncryptionManager`

Default implementation of message encryption manager using RSA encryption for secure key exchange.

#### Key Features

- **RSA Encryption**: Asymmetric encryption for secure key exchange
- **AES Symmetric**: Fast symmetric encryption for message content
- **Digital Signatures**: Message integrity verification
- **Key Management**: Automatic key generation and exchange

---

### 🏗️ MethodCallMessageBuilder
**Namespace:** `CoreRemoting.RpcMessaging`  
**Interfaces:** `IMethodCallMessageBuilder`

Default implementation of method call message builder that converts method metadata to RPC messages.

#### Key Methods

| Method | Return Type | Description |
|--------|-------------|-------------|
| `BuildMethodCallMessage(MethodInfo method, object[] args, Guid uniqueCallKey, string serviceName)` | `MethodCallMessage` | Builds method call message |

---

## Message Types

### 🏗️ WireMessage
**Namespace:** `CoreRemoting.RpcMessaging`  
**Attributes:** `[Serializable]`

Base message type for all wire communications. Contains envelope information for message routing and security.

#### Key Properties

| Property | Type | Description |
|----------|------|-------------|
| `MessageType` | `string` | Type of message (e.g., "rpc", "auth", "goodbye") |
| `Data` | `byte[]` | Serialized message content |
| `UniqueCallKey` | `byte[]` | Unique identifier for RPC call correlation |
| `Iv` | `byte[]` | Initialization vector for encryption |
| `Error` | `bool` | Whether message contains error information |

---

### 🏗️ MethodCallMessage
**Namespace:** `CoreRemoting.RpcMessaging`  
**Attributes:** `[Serializable]`

Message containing method call information for remote procedure invocation.

#### Key Properties

| Property | Type | Description |
|----------|------|-------------|
| `UniqueCallKey` | `Guid` | Unique identifier for method call |
| `ServiceName` | `string` | Name of target service |
| `MethodName` | `string` | Name of method to invoke |
| `Parameters` | `MethodCallParameterMessage[]` | Array of method parameters |
| `ParameterTypes` | `string[]` | Type names of parameters |
| `OneWay` | `bool` | Whether call is one-way (no response expected) |
| `CallContext` | `CallContextEntry[]` | Call context data for implicit flow |

---

### 🏗️ MethodCallResultMessage
**Namespace:** `CoreRemoting.RpcMessaging`  
**Attributes:** `[Serializable]`

Message containing the result of a remote method invocation.

#### Key Properties

| Property | Type | Description |
|----------|------|-------------|
| `ReturnValue` | `object` | Return value from method |
| `OutParameters` | `MethodCallOutParameterMessage[]` | Out and ref parameter values |
| `CallContext` | `CallContextEntry[]` | Updated call context data |

---

### 🏗️ MethodCallParameterMessage
**Namespace:** `CoreRemoting.RpcMessaging`  
**Attributes:** `[Serializable]`

Represents a single method parameter in a method call.

#### Key Properties

| Property | Type | Description |
|----------|------|-------------|
| `Value` | `object` | Parameter value |

---

### 🏗️ MethodCallOutParameterMessage
**Namespace:** `CoreRemoting.RpcMessaging`  
**Attributes:** `[Serializable]`

Represents an out or ref parameter value in method call result.

#### Key Properties

| Property | Type | Description |
|----------|------|-------------|
| `Value` | `object` | Out or ref parameter value |

---

### 🏗️ RemoteDelegateInvocationMessage
**Namespace:** `CoreRemoting.RpcMessaging`  
**Attributes:** `[Serializable]`

Message for invoking delegates remotely from server to client (callback/events).

#### Key Properties

| Property | Type | Description |
|----------|------|-------------|
| `HandlerKey` | `string` | Unique key identifying the delegate handler |
| `DelegateArguments` | `object[]` | Arguments to pass to the delegate |

---

### 🏗️ GoodbyeMessage
**Namespace:** `CoreRemoting.RpcMessaging`  
**Attributes:** `[Serializable]`

Message sent when a client or server is gracefully disconnecting.

#### Key Properties

| Property | Type | Description |
|----------|------|-------------|
| `SessionId` | `Guid` | Session identifier being terminated |

---

## Message Flow

### RPC Call Flow

```
Client                                     Server
  |                                          |
  |-- MethodCallMessage -------------------->|
  |                                          |
  |<-- MethodCallResultMessage --------------|
```

### Authentication Flow

```
Client                                      Server
  |                                           |
  |-- AuthenticationRequestMessage ---------->|
  |                                           |
  |<-- AuthenticationResponseMessage ---------|
```

### Event/Callback Flow

```
Client                                       Server
  |                                           |
  |<-- RemoteDelegateInvocationMessage -------|
  |                                           |
  |-- MethodCallMessage --------------------->|
```

### Session Termination Flow

```
Client                                     Server
  |                                          |
  |-- GoodbyeMessage ----------------------->|
  |                                          |
  |<-- GoodbyeMessage ---------------------->|
```

---

## Message Security

### Encryption Process

1. **Generate Session Keys**: Each session gets unique symmetric key
2. **RSA Key Exchange**: Asymmetric encryption for secure key transfer
3. **Message Encryption**: AES encryption for message content
4. **Digital Signatures**: RSA signatures for integrity verification
5. **Initialization Vectors**: Unique IV per message

### Security Features

| Feature | Description |
|---------|-------------|
| **End-to-End Encryption** | Messages encrypted from client to server |
| **Perfect Forward Secrecy** | New session keys for each session |
| **Message Integrity** | Digital signatures prevent tampering |
| **Replay Protection** | Unique call keys prevent replay attacks |

---

## See Also

- [CoreRemoting](CoreRemoting.md) - Core client and server classes
- [CoreRemoting.Serialization](CoreRemoting-Serialization.md) - Message serialization
- [CoreRemoting.Authentication](CoreRemoting-Authentication.md) - Authentication messages
- [CoreRemoting.RemoteDelegates](CoreRemoting-RemoteDelegates.md) - Remote delegates