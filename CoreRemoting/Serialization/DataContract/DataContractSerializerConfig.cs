using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;

namespace CoreRemoting.Serialization.DataContract
{
    public class DataContractSerializerConfig
    {
        public DataContractSerializerConfig()
        {
            Encoding = Encoding.UTF8;
        }

        public Encoding Encoding { get; set; }
        
        [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global")]
        public IEnumerable<Type> KnownTypes { get; set; }
        
        internal DataContractSerializerSettings ToDataContractSerializerSettings()
        {
            var knownTypes =
                KnownTypes ?? new List<Type>();
            
            return new DataContractSerializerSettings()
            {
                SerializeReadOnlyTypes = true,
                PreserveObjectReferences = true,
                KnownTypes = knownTypes.ToArray()
            };
        }
    }
}