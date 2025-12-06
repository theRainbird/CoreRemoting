/*
 * Code is copied from https://github.com/zyanfx/SafeDeserializationHelpers
 * Many thanks to yallie for this great extensions to make BinaryFormatter a lot safer.
 */

namespace CoreRemoting.Serialization.Binary
{
    using System;
    using System.Runtime.Serialization;
    using System.Security;
    using System.Security.Permissions;

    /// <summary>
    /// Exception to be thrown when possible deserialization vulnerability is detected.
    /// </summary>
    [Serializable]
    public class UnsafeDeserializationException : SecurityException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="UnsafeDeserializationException"/> class.
        /// </summary>
        public UnsafeDeserializationException()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="UnsafeDeserializationException"/> class.
        /// </summary>
        /// <param name="message">Exception message.</param>
        public UnsafeDeserializationException(string message)
            : base(message)
        {
        }

        /// <inheritdoc cref="SecurityException"/>
        protected UnsafeDeserializationException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }

        /// <inheritdoc cref="SecurityException"/>
        [SecurityPermission(SecurityAction.LinkDemand, Flags = SecurityPermissionFlag.SerializationFormatter)]
        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            info.SetType(typeof(SecurityException));
            base.GetObjectData(info, context);
        }
    }
}