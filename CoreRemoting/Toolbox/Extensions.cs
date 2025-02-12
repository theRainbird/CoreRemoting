using System;
using System.Collections.Concurrent;

namespace CoreRemoting.Toolbox
{
    /// <summary>
    /// Extension methods.
    /// </summary>
    public static class Extensions
    {
        private static ConcurrentDictionary<Type, object> DefaultValues = new();

        /// <summary>
        /// Gets the default value for the given type.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <returns>default() for the type.</returns>
        public static object GetDefaultValue(this Type type)
        {
            if (type == typeof(void) || !type.IsValueType)
            {
                return null;
            }

            return DefaultValues.GetOrAdd(type, Activator.CreateInstance);
        }
    }
}
