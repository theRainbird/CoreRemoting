using System.Runtime.Serialization.Formatters;

namespace CoreRemoting.Serialization.Binary
{
    /// <summary>
    /// Describes binary serialization settings.
    /// </summary>
    public class BinarySerializerConfig
    {
        /// <summary>
        /// Creates a new instance of the BinarySerializerConfig class.
        /// </summary>
        public BinarySerializerConfig()
        {
            TypeFormat = FormatterTypeStyle.TypesWhenNeeded;
            FilterLevel = TypeFilterLevel.Full;
            SerializeAssemblyVersions = false;
        }
        
        /// <summary>
        /// Gets or sets the style how types should be formatted.
        /// </summary>
        public FormatterTypeStyle TypeFormat { get; set; }
        
        /// <summary>
        /// Gets or sets the type filter level for security reasons.
        /// </summary>
        public TypeFilterLevel FilterLevel { get; set; }
        
        /// <summary>
        /// Gets or sets whether assembly versions should be serialized or not.
        /// </summary>
        public bool SerializeAssemblyVersions { get; set; }
    }
}