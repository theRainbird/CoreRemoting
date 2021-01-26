using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;

namespace CoreRemoting.Serialization.DataContract
{
    /// <summary>
    /// Describes data contract serialization settings.
    /// </summary>
    public class DataContractSerializerConfig
    {
        /// <summary>
        /// Creates a new instance of the DataContractSerializerConfig class.
        /// </summary>
        public DataContractSerializerConfig()
        {
            Encoding = Encoding.UTF8;
        }

        /// <summary>
        /// Gets or sets the encoding to be used for serialization.
        /// </summary>
        public Encoding Encoding { get; set; }
        
        /// <summary>
        /// Gets or sets a list on known types that are safe for deserialization.
        /// </summary>
        [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global")]
        [SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
        public IEnumerable<Type> KnownTypes { get; set; }
        
        /// <summary>
        /// Convert to DataContractSerializerSettings object.
        /// </summary>
        /// <returns>DataContractSerializerSettings to be used with a DataContractSerializer object</returns>
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