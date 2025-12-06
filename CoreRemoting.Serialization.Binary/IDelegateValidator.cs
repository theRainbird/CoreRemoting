/*
 * Code is copied from https://github.com/zyanfx/SafeDeserializationHelpers
 * Many thanks to yallie for this great extensions to make BinaryFormatter a lot safer.
 */

namespace CoreRemoting.Serialization.Binary
{
    using System;

    /// <summary>
    /// Interface for validating the deserialized delegates.
    /// </summary>
    public interface IDelegateValidator
    {
        /// <summary>
        /// Validates the given delegate.
        /// Throws exceptions for the unsafe delegates found in the invocation list.
        /// </summary>
        /// <param name="del">The delegate to validate.</param>
        void ValidateDelegate(Delegate del);
    }
}