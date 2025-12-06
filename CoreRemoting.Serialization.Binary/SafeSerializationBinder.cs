/*
 * Code is copied from https://github.com/zyanfx/SafeDeserializationHelpers
 * Many thanks to yallie for this great extensions to make BinaryFormatter a lot safer.
 */

namespace CoreRemoting.Serialization.Binary
{
    using System;
    using System.Runtime.Serialization;

    /// <inheritdoc cref="SerializationBinder" />
    internal sealed class SafeSerializationBinder : SerializationBinder
    {
        /// <summary>
        /// Core library assembly name.
        /// </summary>
        public const string CORE_LIBRARY_ASSEMBLY_NAME = "mscorlib";

        /// <summary>
        /// System.DelegateSerializationHolder type name.
        /// </summary>
        public const string DELEGATE_SERIALIZATION_HOLDER_TYPE_NAME = "System.DelegateSerializationHolder";

        /// <summary>
        /// Initializes a new instance of the <see cref="SafeSerializationBinder"/> class.
        /// </summary>
        /// <param name="nextBinder">Next serialization binder in chain.</param>
        public SafeSerializationBinder(SerializationBinder nextBinder = null)
        {
            NextBinder = nextBinder;
        }

        private SerializationBinder NextBinder { get; }

        /// <inheritdoc cref="SerializationBinder" />
        public override Type BindToType(string assemblyName, string typeName)
        {
            // prevent delegate deserialization attack
            if (typeName == DELEGATE_SERIALIZATION_HOLDER_TYPE_NAME &&
                assemblyName.StartsWith(CORE_LIBRARY_ASSEMBLY_NAME, StringComparison.InvariantCultureIgnoreCase))
            {
                return typeof(CustomDelegateSerializationHolder);
            }

            // suppress known blacklisted types
            TypeNameValidator.Default.ValidateTypeName(assemblyName, typeName);

            // chain to the original type binder if exists
            return NextBinder?.BindToType(assemblyName, typeName);
        }
    }
}