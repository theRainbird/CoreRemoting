using System;
using System.Diagnostics.CodeAnalysis;

namespace CoreRemoting.Serialization
{
    [SuppressMessage("ReSharper", "ClassNeverInstantiated.Global")]
    [SuppressMessage("ReSharper", "UnusedMember.Local")]
    [SuppressMessage("ReSharper", "RedundantAttributeUsageProperty")]
    [AttributeUsage(AttributeTargets.Interface | AttributeTargets.Method | AttributeTargets.Property, Inherited = true, AllowMultiple = true)]
    public sealed class ServiceKnownTypeAttribute : Attribute
    {
        private ServiceKnownTypeAttribute() { }

        public ServiceKnownTypeAttribute(Type type)
        {
            Type = type;
        }

        public Type Type { get; }
    }
}