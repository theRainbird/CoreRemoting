using System;
using System.Linq;
using System.Reflection;
using CoreRemoting.Authentication;
using CoreRemoting.Channels;
using CoreRemoting.Channels.Websocket;
using CoreRemoting.Serialization;
using CoreRemoting.Serialization.Binary;
using CoreRemoting.Serialization.DataContract;

namespace CoreRemoting.ClassicRemotingApi.ConfigSection
{
    public static class ConfigSectionExtensionMethods
    {
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
                uniqueServerInstanceName: configElement.UniqueInstanceName);

            return entry;
        }

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
                UniqueServerInstanceName = configElement.UniqueInstanceName
            };

            return serverConfig;
        }

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

        private static IServerChannel CreateServerChannelFromConfigName(string channelTypeName)
        {
            var websocketServerChannelShortcuts = 
                new string[] {"ws", "websocket"};

            if (websocketServerChannelShortcuts.Contains(channelTypeName.ToLower()))
                return new WebsocketServerChannel();

            var channelType = GetTypeFromConfigString(channelTypeName);
            
            return (IServerChannel) Activator.CreateInstance(channelType);
        }
        
        private static ISerializerAdapter CreateSerializerAdapterFromConfigName(string serializerName)
        {
            var binarySerializerShortcuts = 
                new[]
                {
                    "binary", 
                    "binaryformatter", 
                };

            var dataContractSerializerShortcusts =
                new[]
                {
                    "datacontract", 
                    "datacontracts", 
                    "datacontractserializer", 
                };
            
            if (binarySerializerShortcuts.Contains(serializerName.ToLower()))
                return new BinarySerializerAdapter();

            if (dataContractSerializerShortcusts.Contains(serializerName.ToLower()))
                return new DataContractSerializerAdapter();
            
            var serializerAdapterType = GetTypeFromConfigString(serializerName);
            
            return (ISerializerAdapter) Activator.CreateInstance(serializerAdapterType);
        }
    }
}