## Serialization

Objects cannot be sent over the network as they are. They need to be serialized into a byte array in order to be transferred. 
After received, the byte array must be deserialized into an object again.
CoreRemoting does all this serialization work for you automatically.

To make our life more exciting, there are different ways to serialize C# objects. And each of these different ways have advantages and disadvantages. The serializers, that implement that different ways of serialization, are not hard coded into CoreRemoting. Serializers are integrated via an adapter component instead. Such an adapter must implement the [ISerializerAdapter](https://github.com/theRainbird/CoreRemoting/wiki/API-Reference#iserializeradapter) interface.

CoreRemoting supports the following serializers out of the box: 
* BsonSerializerAdapter: BSON _(with JSON.NET)_; Serializes almost every type into a [Binary JSON](http://bsonspec.org/) stream
* NeoBinarySerializerAdapter: Serializes any type interface into a byte stream

You can tell CoreRemoting which serializer should be used via configuration (ClientConfig / ServerConfig). 
Just create an instance of the serializer adapter of your choice and assign it to Serializer property. 

**Important!**
Client and server must use the same serializer type in order to understand each others messages.

### BSON Serializer

If you don't specify a serializer, BSON serializer is used by default. BSON is a modern serialization format. It's faster and the serialized data is smaller than regular JSON. The fact that it is not human readable and is not supported by web browsers and Javascript interpreters doesn't matter, because CoreRemoting communicates from .NET to .NET and doesn't support Javascript clients. If you write a new application and have no very special requirements BSON serializer should be a good choice in most cases.
You can extend the BSON serializer with custom [JSON Converters](https://www.newtonsoft.com/json/help/html/CustomJsonConverter.htm)_(they work also for BSON)_ to control how specified types are serialized. Just set your needed JSON Converters at [BsonSerializerConfig](https://github.com/theRainbird/CoreRemoting/wiki/API-Reference#bsonserializerconfig) object and pass this to the BsonSerializerAdapter.

### Neo-Binary Serializer

The second serializer that is supported out of the box, is the NeoBinarySerializer. 
Because classic BinaryFormatter is deprecated, NeoBinarySerializer is a replacement.
It provides fast and reliable binary serialization.

### Classic BinaryFormatter

The deprecated classic BinaryFormatter is available the separate CoreRemoting.Serialization.Binary Nuget package. 
It has been around since .NET Framework 1.0 and it's still there but no longer supported by Microsoft.
Use it only if you're migrating a existing .NET Remoting application to CoreRemoting and want maximum compatibility

The [BinarySerializerConfig](https://github.com/theRainbird/CoreRemoting/wiki/API-Reference#binaryserializerconfig) object can be used to pass custom configuration like TypeFilterLevel to the BinarySerializerAdapter instance.


### Other Serializers?

If none of the above described serializers fits your needs, you could integrate the serializer of your choice easily. Just implement [ISerializerAdapter](https://github.com/theRainbird/CoreRemoting/wiki/API-Reference#iserializeradapter) interface. 
You can use the source code of the existing serializer adapters as inspiration.

## Cross framework serialization

To support deserialization of .NET Core/.NET 5+ types on a .NET Framework 4.x process, add the following line in your Main method:
`
CrossFrameworkSerialization.RedirectPrivateCoreLibToMscorlib();
`
To support deserialization of .NET Framework 4.x types on a .NET Core/.NET 5+ process,  use:
`
CrossFrameworkSerialization.RedirectMscorlibToPrivateCoreLib();
`
 