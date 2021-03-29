using System.Configuration;

namespace CoreRemoting.ClassicRemotingApi.ConfigSection
{
    /// <summary>
    /// Defines a configuration section for CoreRemoting configuration in a XML config file.
    /// </summary>
    // ReSharper disable once ClassNeverInstantiated.Global
    public class CoreRemotingConfigSection : ConfigurationSection
    {
        /// <summary>
        /// Gets a collection of configured CoreRemoting server instances.
        /// </summary>
        [ConfigurationProperty("serverInstances", IsRequired = false)]
        [ConfigurationCollection(typeof(ServerInstanceConfigElementCollection))]
        public ServerInstanceConfigElementCollection ServerInstances => 
            (ServerInstanceConfigElementCollection)this["serverInstances"];
        
        /// <summary>
        /// Gets a collection of configured CoreRemoting services.
        /// </summary>
        [ConfigurationProperty("services", IsRequired = false)]
        [ConfigurationCollection(typeof(WellKnownServiceConfigElementCollection))]
        public WellKnownServiceConfigElementCollection Services => 
            (WellKnownServiceConfigElementCollection)this["services"];
        
        /// <summary>
        /// Gets a collection of configured CoreRemoting client instances.
        /// </summary>
        [ConfigurationProperty("clientInstances", IsRequired = false)]
        [ConfigurationCollection(typeof(ClientInstanceConfigElementCollection))]
        public ClientInstanceConfigElementCollection ClientInstances => 
            (ClientInstanceConfigElementCollection)this["clientInstances"];
    }
}