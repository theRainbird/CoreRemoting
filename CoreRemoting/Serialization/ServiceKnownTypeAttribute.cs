using System;
using System.Diagnostics.CodeAnalysis;

namespace CoreRemoting.Serialization
{
    /// <summary>
    /// Attribute to specify a known type that are safe for deserialization for the service this attribute is put on. 
    /// </summary>
    [SuppressMessage("ReSharper", "ClassNeverInstantiated.Global")]
    [SuppressMessage("ReSharper", "UnusedMember.Local")]
    [SuppressMessage("ReSharper", "RedundantAttributeUsageProperty")]
    [AttributeUsage(AttributeTargets.Interface | AttributeTargets.Method | AttributeTargets.Property, Inherited = true, AllowMultiple = true)]
    public sealed class ServiceKnownTypeAttribute : Attribute
    {
        /// <summary>
        /// Creates a new instance of the ServiceKnownTypeAttribute class.
        /// </summary>
        private ServiceKnownTypeAttribute() { }

        /// <summary>
        /// Creates a new instance of the ServiceKnownTypeAttribute class.
        /// </summary>
        /// <param name="type">Known type that is safe for deserialization</param>
        public ServiceKnownTypeAttribute(Type type)
        {
            Type = type;
        }

        /// <summary>
        /// Gets the known type that is safe for deserialization.
        /// </summary>
        public Type Type { get; }
    }
}