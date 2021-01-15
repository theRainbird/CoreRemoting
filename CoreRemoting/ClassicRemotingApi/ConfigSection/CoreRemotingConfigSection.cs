using System.Configuration;

namespace CoreRemoting.ClassicRemotingApi.ConfigSection
{
    public class CoreRemotingConfigSection : ConfigurationSection
    {
        [ConfigurationProperty("serverInstances", IsRequired = false)]
        [ConfigurationCollection(typeof(ServerInstanceConfigElementCollection))]
        public ServerInstanceConfigElementCollection ServerInstances => 
            (ServerInstanceConfigElementCollection)this["serverInstances"];
        
        [ConfigurationProperty("services", IsRequired = false)]
        [ConfigurationCollection(typeof(WellKnownServiceConfigElementCollection))]
        public WellKnownServiceConfigElementCollection Services => 
            (WellKnownServiceConfigElementCollection)this["services"];
    }
}