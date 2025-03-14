using System.Runtime.CompilerServices;

namespace CoreRemoting.Toolbox;

/// <summary>
/// Helper class to add attachment properties to objects.
/// </summary>
/// <remarks>
/// Inspired with Overby.Extensions.Attachments by Ronnie Overby:
/// https://github.com/ronnieoverby/Overby.Extensions.Attachments
/// </remarks>
internal static class AttachmentHelper
{
    /// <summary>
    /// Optional value container.
    /// </summary>
    /// <typeparam name="TValue">The type of the value</typeparam>
    private class Optional<TValue>
    {
        public TValue Value { get; set; }
        public bool IsSet { get; set; }
    }

    /// <summary>
    /// Value attachment handler.
    /// </summary>
    /// <typeparam name="TValue">The type of the value.</typeparam>
    /// <typeparam name="TPropertyName">The name of the property.</typeparam>
    private class Attachment<TValue, TPropertyName>
    {
        private static ConditionalWeakTable<object, Optional<TValue>> Values { get; } = new();

        public static bool Get(object self, out TValue value)
        {
            var result = Values.TryGetValue(self, out var opt);
            value = (result && opt.IsSet) ? opt.Value : default;
            return result;
        }

        public static void Set(object self, TValue value)
        {
            var opt = Values.GetOrCreateValue(self);
            opt.Value = value;
            opt.IsSet = true;
        }
    }

    /// <summary>
    /// Gets the attached property value.
    /// </summary>
    /// <typeparam name="TValue">Property type.</typeparam>
    /// <typeparam name="TPropName">Property name.</typeparam>
    /// <param name="self">Object with attached property.</param>
    /// <param name="value">Property value.</param>
    /// <returns>True, if value existed, otherwise, false.</returns>
    public static bool Get<TValue, TPropName>(this object self, out TValue value) =>
        Attachment<TValue, TPropName>.Get(self, out value);

    /// <summary>
    /// Gets the anonymous attached property value.
    /// </summary>
    /// <typeparam name="TValue">Property type.</typeparam>
    /// <param name="self">Object with attached property.</param>
    /// <param name="value">Property value.</param>
    /// <returns>True, if value existed, otherwise, false.</returns>
    public static bool Get<TValue>(this object self, out TValue value) =>
        Get<TValue, TValue>(self, out value);

    /// <summary>
    /// Sets the attached property value.
    /// </summary>
    /// <typeparam name="TValue">Property type.</typeparam>
    /// <typeparam name="TPropName">Property name.</typeparam>
    /// <param name="self">Object with attached property.</param>
    /// <param name="value">Property value.</param>
    public static void Set<TValue, TPropName>(this object self, TValue value) =>
        Attachment<TValue, TPropName>.Set(self, value);

    /// <summary>
    /// Sets the anonymous attached property value.
    /// </summary>
    /// <typeparam name="TValue">Property type.</typeparam>
    /// <param name="self">Object with attached property.</param>
    /// <param name="value">Property value.</param>
    public static void Set<TValue>(this object self, TValue value) =>
        Set<TValue, TValue>(self, value);
}

