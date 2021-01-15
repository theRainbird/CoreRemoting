using System.Configuration;

namespace CoreRemoting.ClassicRemotingApi.ConfigSection
{
    public class ServerInstanceConfigElement : ConfigurationElement
    {
        [ConfigurationProperty("uniqueInstanceName", IsRequired = true, DefaultValue = "", IsKey = true)]
        public string UniqueInstanceName
        {
            get => (string)base["uniqueInstanceName"];
            set => base["uniqueInstanceName"] = value;
        }
        
        [ConfigurationProperty("hostName", IsRequired = false, DefaultValue = "localhost")]
        public string HostName
        {
            get => (string)base["hostName"];
            set => base["hostName"] = value;
        }
        
        [ConfigurationProperty("networkPort", IsRequired = false, DefaultValue = 9090)]
        [IntegerValidator(MinValue = 0, MaxValue = 65535, ExcludeRange = false)]
        public int NetworkPort
        {
            get => (int)base["networkPort"];
            set => base["networkPort"] = value;
        }
        
        [ConfigurationProperty("keySize", IsRequired = false, DefaultValue = 4096)]
        public int KeySize
        {
            get => (int)base["keySize"];
            set => base["keySize"] = value;
        }
        
        [ConfigurationProperty("serializer", IsRequired = false, DefaultValue = "binary")]
        public string Serializer
        {
            get => (string)base["serializer"];
            set => base["serializer"] = value;
        }
        
        [ConfigurationProperty("channel", IsRequired = false, DefaultValue = "ws")]
        public string Channel
        {
            get => (string)base["channel"];
            set => base["channel"] = value;
        }
        
        [ConfigurationProperty("authenticationProvider", IsRequired = false, DefaultValue = "")]
        public string AuthenticationProvider
        {
            get => (string)base["authenticationProvider"];
            set => base["authenticationProvider"] = value;
        }
        
        [ConfigurationProperty("authenticationRequired", IsRequired = false, DefaultValue = false)]
        public bool AuthenticationRequired
        {
            get => (bool)base["authenticationRequired"];
            set => base["authenticationRequired"] = value;
        }
        
        [ConfigurationProperty("messageEncryption", IsRequired = false, DefaultValue = true)]
        public bool MessageEncryption
        {
            get => (bool)base["messageEncryption"];
            set => base["messageEncryption"] = value;
        }
    }
}