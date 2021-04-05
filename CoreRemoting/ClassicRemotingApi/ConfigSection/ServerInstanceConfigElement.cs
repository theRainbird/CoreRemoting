using System.Configuration;

namespace CoreRemoting.ClassicRemotingApi.ConfigSection
{
    /// <summary>
    /// Configuration element for a CoreRemoting server instance.
    /// </summary>
    public class ServerInstanceConfigElement : ConfigurationElement
    {
        /// <summary>
        /// Gets or sets the unique name of the server instance.
        /// </summary>
        [ConfigurationProperty("uniqueInstanceName", IsRequired = true, DefaultValue = "", IsKey = true)]
        public string UniqueInstanceName
        {
            get => (string)base["uniqueInstanceName"];
            set => base["uniqueInstanceName"] = value;
        }
        
        /// <summary>
        /// Gets or sets the hostname the server instance is bound to.
        /// </summary>
        [ConfigurationProperty("hostName", IsRequired = false, DefaultValue = "localhost")]
        public string HostName
        {
            get => (string)base["hostName"];
            set => base["hostName"] = value;
        }
        
        /// <summary>
        /// Gets or sets the network port on which the server instance is listening on.
        /// </summary>
        [ConfigurationProperty("networkPort", IsRequired = false, DefaultValue = 9090)]
        [IntegerValidator(MinValue = 0, MaxValue = 65535, ExcludeRange = false)]
        public int NetworkPort
        {
            get => (int)base["networkPort"];
            set => base["networkPort"] = value;
        }
        
        /// <summary>
        /// Gets or sets the RSA key size for message encryption.
        /// </summary>
        [ConfigurationProperty("keySize", IsRequired = false, DefaultValue = 4096)]
        public int KeySize
        {
            get => (int)base["keySize"];
            set => base["keySize"] = value;
        }
        
        /// <summary>
        /// Gets or sets the name of the serializer which should be used by the server instance.
        /// </summary>
        [ConfigurationProperty("serializer", IsRequired = false, DefaultValue = "binary")]
        public string Serializer
        {
            get => (string)base["serializer"];
            set => base["serializer"] = value;
        }
        
        /// <summary>
        /// Gets or sets the type of the server channel which should be used for communication.
        /// </summary>
        [ConfigurationProperty("channel", IsRequired = false, DefaultValue = "ws")]
        public string Channel
        {
            get => (string)base["channel"];
            set => base["channel"] = value;
        }
        
        /// <summary>
        /// Gets or sets the type of authentication provider which should be used to authenticate client credentials.
        /// </summary>
        [ConfigurationProperty("authenticationProvider", IsRequired = false, DefaultValue = "")]
        public string AuthenticationProvider
        {
            get => (string)base["authenticationProvider"];
            set => base["authenticationProvider"] = value;
        }
        
        /// <summary>
        /// Gets or sets whether authentication is required or not.
        /// </summary>
        [ConfigurationProperty("authenticationRequired", IsRequired = false, DefaultValue = false)]
        public bool AuthenticationRequired
        {
            get => (bool)base["authenticationRequired"];
            set => base["authenticationRequired"] = value;
        }
        
        /// <summary>
        /// Gets or set whether messages should be encrypted or not.
        /// </summary>
        [ConfigurationProperty("messageEncryption", IsRequired = false, DefaultValue = true)]
        public bool MessageEncryption
        {
            get => (bool)base["messageEncryption"];
            set => base["messageEncryption"] = value;
        }
        
        /// <summary>
        /// Gets or set whether this is the default server.
        /// </summary>
        [ConfigurationProperty("isDefault", IsRequired = false, DefaultValue = false)]
        public bool IsDefault
        {
            get => (bool)base["isDefault"];
            set => base["isDefault"] = value;
        }
    }
}