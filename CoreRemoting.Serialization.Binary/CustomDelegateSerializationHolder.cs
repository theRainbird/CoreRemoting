/*
 * Code is copied from https://github.com/zyanfx/SafeDeserializationHelpers
 * Many thanks to yallie for this great extensions to make BinaryFormatter a lot safer.
 */

namespace CoreRemoting.Serialization.Binary
{
    using System;
    using System.Reflection;
    using System.Runtime.Serialization;
    using System.Security.Permissions;

    /// <summary>
    /// Custom replacement for the DelegateSerializationHolder featuring delegate validation.
    /// </summary>
    [Serializable]
    internal sealed class CustomDelegateSerializationHolder : ISerializable, IObjectReference
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="CustomDelegateSerializationHolder"/> class.
        /// </summary>
        /// <param name="info">Serialization info.</param>
        /// <param name="context">Streaming context</param>
        private CustomDelegateSerializationHolder(SerializationInfo info, StreamingContext context)
        {
            Holder = (IObjectReference)Constructor.Invoke(new object[] { info, context });
        }

        private static Type DelegateSerializationHolderType { get; } = Type.GetType(SafeSerializationBinder.DELEGATE_SERIALIZATION_HOLDER_TYPE_NAME);

        private static ConstructorInfo Constructor { get; } = DelegateSerializationHolderType.GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            null,
            new[] { typeof(SerializationInfo), typeof(StreamingContext) },
            null);

        private IObjectReference Holder { get; set; }

        /// <inheritdoc cref="ISerializable" />
        [SecurityPermission(SecurityAction.LinkDemand, Flags = SecurityPermissionFlag.SerializationFormatter)]
        public void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc cref="IObjectReference" />
        [SecurityPermission(SecurityAction.LinkDemand, Flags = SecurityPermissionFlag.SerializationFormatter)]
        public object GetRealObject(StreamingContext context)
        {
            var result = Holder.GetRealObject(context);
            if (result is Delegate del)
            {
                DelegateValidator.Default.ValidateDelegate(del);
            }

            return result;
        }
    }
}