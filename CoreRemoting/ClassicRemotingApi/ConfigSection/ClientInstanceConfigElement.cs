using System.Configuration;

namespace CoreRemoting.ClassicRemotingApi.ConfigSection
{
    /// <summary>
    /// Configuration element for a CoreRemoting client instance.
    /// </summary>
    public class ClientInstanceConfigElement : ConfigurationElement
    {
        /// <summary>
        /// Gets or sets the unique name of the client instance.
        /// </summary>
        [ConfigurationProperty("uniqueInstanceName", IsRequired = true, DefaultValue = "", IsKey = true)]
        public string UniqueInstanceName
        {
            get => (string)base["uniqueInstanceName"];
            set => base["uniqueInstanceName"] = value;
        }
        
        /// <summary>
        /// Gets or sets the server hostname.
        /// </summary>
        [ConfigurationProperty("serverHostName", IsRequired = false, DefaultValue = "localhost")]
        public string ServerHostName
        {
            get => (string)base["serverHostName"];
            set => base["serverHostName"] = value;
        }
        
        /// <summary>
        /// Gets or sets the server network port.
        /// </summary>
        [ConfigurationProperty("serverPort", IsRequired = false, DefaultValue = 9090)]
        [IntegerValidator(MinValue = 0, MaxValue = 65535, ExcludeRange = false)]
        public int ServerPort
        {
            get => (int)base["serverPort"];
            set => base["serverPort"] = value;
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
        /// Gets or sets the name of the serializer which should be used by the client instance.
        /// </summary>
        [ConfigurationProperty("serializer", IsRequired = false, DefaultValue = "binary")]
        public string Serializer
        {
            get => (string)base["serializer"];
            set => base["serializer"] = value;
        }
        
        /// <summary>
        /// Gets or sets the type of the client channel which should be used for communication.
        /// </summary>
        [ConfigurationProperty("channel", IsRequired = false, DefaultValue = "ws")]
        public string Channel
        {
            get => (string)base["channel"];
            set => base["channel"] = value;
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
        /// Gets or set whether this is the default client.
        /// </summary>
        [ConfigurationProperty("isDefault", IsRequired = false, DefaultValue = false)]
        public bool IsDefault
        {
            get => (bool)base["isDefault"];
            set => base["isDefault"] = value;
        }
        
        /// <summary>
        /// Gets or sets the connection timeout in seconds (0 means infinite).
        /// </summary>
        [ConfigurationProperty("connectionTimeout", IsRequired = false, DefaultValue = 120)]
        public int ConnectionTimeout
        {
            get => (int)base["connectionTimeout"];
            set => base["connectionTimeout"] = value;
        }
        
        /// <summary>
        /// Gets or sets the authentication timeout in seconds (0 means infinite).
        /// </summary>
        [ConfigurationProperty("authenticationTimeout", IsRequired = false, DefaultValue = 30)]
        public int AuthenticationTimeout
        {
            get => (int)base["authenticationTimeout"];
            set => base["authenticationTimeout"] = value;
        }

        /// <summary>
        /// Gets or sets the invocation timeout in seconds (0 means infinite).
        /// </summary>
        [ConfigurationProperty("invocationTimeout", IsRequired = false, DefaultValue = 0)]
        public int InvocationTimeout
        {
            get => (int)base["invocationTimeout"];
            set => base["invocationTimeout"] = value;
        }
    }
}