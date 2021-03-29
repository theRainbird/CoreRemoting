using System.Runtime.Serialization;

namespace CoreRemoting.Tests.ExternalTypes
{
    [DataContract]
    public class DataClass
    {
        [DataMember]
        public int Value { get; set; }
    }
}