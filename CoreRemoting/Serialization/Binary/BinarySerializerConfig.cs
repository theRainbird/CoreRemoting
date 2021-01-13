using System.Runtime.Serialization.Formatters;

namespace CoreRemoting.Serialization.Binary
{
    public class BinarySerializerConfig
    {
        public BinarySerializerConfig()
        {
            TypeFormat = FormatterTypeStyle.TypesWhenNeeded;
            FilterLevel = TypeFilterLevel.Full;
            SerializeAssemblyVersions = false;
        }
        
        public FormatterTypeStyle TypeFormat { get; set; }
        
        public TypeFilterLevel FilterLevel { get; set; }
        
        public bool SerializeAssemblyVersions { get; set; }
    }
}