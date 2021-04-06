using System;
using System.Linq;
using System.Reflection;
using CoreRemoting.Authentication;
using CoreRemoting.Channels;
using CoreRemoting.Channels.Websocket;
using CoreRemoting.Serialization;
using CoreRemoting.Serialization.Binary;
using CoreRemoting.Serialization.Bson;

namespace CoreRemoting.ClassicRemotingApi.ConfigSection
{
    /// <summary>
    /// Extension methods for XML configuration classes.
    /// </summary>
    public static class ConfigSectionExtensionMethods
    {
        /// <summary>
        /// Converts a wellknown service XML config object into a WellknownServiceTypeEntry object.
        /// </summary>
        /// <param name="configElement">Service definition from XML config</param>
        /// <returns>WellknownServiceTypeEntry object</returns>
        public static WellKnownServiceTypeEntry ToWellKnownServiceTypeEntry(
            this WellKnownServiceConfigElement configElement)
        {
            if (configElement == null)
                throw new ArgumentNullException(nameof(configElement));

            var entry = new WellKnownServiceTypeEntry(
                interfaceAssemblyName: configElement.InterfaceAssemblyName,
                interfaceTypeName: configElement.InterfaceTypeName,
                implementationAssemblyName: configElement.ImplementationAssemblyName,
                implementationTypeName: configElement.ImplementationTypeName,
                lifetime: configElement.Lifetime,
                serviceName: configElement.ServiceName,
                uniqueServerInstanceName: configElement.UniqueServerInstanceName);

            return entry;
        }

        /// <summary>
        /// Converts a server XML config object into a ServerConfig object.
        /// </summary>
        /// <param name="configElement">Configuration element</param>
        /// <returns>ServerConfig object</returns>
        public static ServerConfig ToServerConfig(
            this ServerInstanceConfigElement configElement)
        {
            if (configElement == null)
                throw new ArgumentNullException(nameof(configElement));

            var serverConfig = new ServerConfig
            {
                Channel = CreateServerChannelFromConfigName(configElement.Channel),
                Serializer = CreateSerializerAdapterFromConfigName(configElement.Serializer),
                AuthenticationProvider = 
                    string.IsNullOrWhiteSpace(configElement.AuthenticationProvider)
                        ? null
                        : (IAuthenticationProvider)Activator.CreateInstance(GetTypeFromConfigString(configElement.AuthenticationProvider)),
                AuthenticationRequired = configElement.AuthenticationRequired,
                HostName = configElement.HostName,
                KeySize = configElement.KeySize,
                MessageEncryption = configElement.MessageEncryption,
                NetworkPort = configElement.NetworkPort,
                UniqueServerInstanceName = configElement.UniqueInstanceName,
                IsDefault = configElement.IsDefault
            };

            return serverConfig;
        }

        /// <summary>
        /// Converts a client XML config object into a ClientConfig object.
        /// </summary>
        /// <param name="configElement">Configuration element</param>
        /// <returns>ClientConfig object</returns>
        public static ClientConfig ToClientConfig(
            this ClientInstanceConfigElement configElement)
        {
            if (configElement == null)
                throw new ArgumentNullException(nameof(configElement));

            var clientConfig = new ClientConfig()
            {
                Channel = CreateClientChannelFromConfigName(configElement.Channel),
                Serializer = CreateSerializerAdapterFromConfigName(configElement.Serializer),
                ServerHostName = configElement.ServerHostName,
                ServerPort = configElement.ServerPort,
                KeySize = configElement.KeySize,
                MessageEncryption = configElement.MessageEncryption,
                UniqueClientInstanceName = configElement.UniqueInstanceName,
                IsDefault = configElement.IsDefault,
                ConnectionTimeout = configElement.ConnectionTimeout,
                AuthenticationTimeout = configElement.AuthenticationTimeout,
                InvocationTimeout = configElement.InvocationTimeout
            };

            return clientConfig;
        }
        
        /// <summary>
        /// Gets a type from a string that contains type name and assembly name.
        /// </summary>
        /// <param name="assemblyAndTypeConfigString">String containing type name and assembly name, separated by a comma</param>
        /// <returns>Type object</returns>
        /// <exception cref="FormatException">Thrown, if string format is invalid</exception>
        private static Type GetTypeFromConfigString(string assemblyAndTypeConfigString)
        {
            var parts = assemblyAndTypeConfigString.Split(',');

            if (parts.Length != 2)
                throw new FormatException(
                    "Unsupported format for type. Use 'TypeName, AssemblyName' format.");

            var assembly = Assembly.Load(parts[1].Trim());
            var type = assembly.GetType(parts[0].Trim());

            return type;
        }

        /// <summary>
        /// Creates a server channel from a type string.
        /// </summary>
        /// <param name="channelTypeName">String containing a channel type shortcut (e.g. "ws" for websockets) or a type name and assembly name, separated by a comma</param>
        /// <returns>Server channel</returns>
        private static IServerChannel CreateServerChannelFromConfigName(string channelTypeName)
        {
            var websocketServerChannelShortcuts = 
                new[] {"ws", "websocket"};

            if (websocketServerChannelShortcuts.Contains(channelTypeName.ToLower()))
                return new WebsocketServerChannel();

            var channelType = GetTypeFromConfigString(channelTypeName);
            
            return (IServerChannel) Activator.CreateInstance(channelType);
        }
        
        /// <summary>
        /// Creates a client channel from a type string.
        /// </summary>
        /// <param name="channelTypeName">String containing a channel type shortcut (e.g. "ws" for websockets) or a type name and assembly name, separated by a comma</param>
        /// <returns>Client channel</returns>
        private static IClientChannel CreateClientChannelFromConfigName(string channelTypeName)
        {
            var websocketServerChannelShortcuts = 
                new[] {"ws", "websocket"};

            if (websocketServerChannelShortcuts.Contains(channelTypeName.ToLower()))
                return new WebsocketClientChannel();

            var channelType = GetTypeFromConfigString(channelTypeName);
            
            return (IClientChannel) Activator.CreateInstance(channelType);
        }
        
        /// <summary>
        /// Creates a serializer adapter from a type string.
        /// </summary>
        /// <param name="serializerName">String containing a serializer (e.g. "binary" for a binary serializer) type shortcut or a type name and assembly name, separated by a comma</param>
        /// <returns></returns>
        private static ISerializerAdapter CreateSerializerAdapterFromConfigName(string serializerName)
        {
            var binarySerializerShortcuts = 
                new[]
                {
                    "binary", 
                    "binaryformatter",
                    "binaryserializer"
                };

            var bsonSerializerShortcusts =
                new[]
                {
                    "bson", 
                    "bsonformatter", 
                    "bsonserializer", 
                };
            
            if (binarySerializerShortcuts.Contains(serializerName.ToLower()))
                return new BinarySerializerAdapter();

            if (bsonSerializerShortcusts.Contains(serializerName.ToLower()))
                return new BsonSerializerAdapter();
            
            var serializerAdapterType = GetTypeFromConfigString(serializerName);
            
            return (ISerializerAdapter) Activator.CreateInstance(serializerAdapterType);
        }
    }
}