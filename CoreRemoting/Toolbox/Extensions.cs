using System;
using System.Collections.Concurrent;
using System.Reflection;

namespace CoreRemoting.Toolbox
{
    /// <summary>
    /// Extension methods.
    /// </summary>
    public static class Extensions
    {
        /// <summary>
        /// Checks whether the given method represents event subscription or unsubscription.
        /// </summary>
        /// <param name="method">Method information.</param>
        /// <param name="eventName">If return value is true, this parameter returns the name of the event.</param>
        /// <param name="subscription">If true, method represents subscription, otherwise, it's unsubscription.</param>
        public static bool IsEventAccessor(this MethodInfo method, out string eventName, out bool subscription)
        {
            // void add_Click(EventHandler e) → subscription to Click
            // void remove_Click(EventHandler e) → unsubscription from Click
            eventName = null;
            subscription = false;

            if (method == null ||
                method.IsGenericMethod ||
                method.ReturnType != typeof(void))
            {
                return false;
            }

            if (method.Name.StartsWith("add_"))
            {
                eventName = method.Name.Substring(4);
                subscription = true;
            }
            else if (method.Name.StartsWith("remove_"))
            {
                eventName = method.Name.Substring(7);
                subscription = false;
            }
            else
            {
                return false;
            }

            var parameters = method.GetParameters();
            if (parameters.Length != 1)
            {
                return false;
            }

            return typeof(Delegate)
                .IsAssignableFrom(parameters[0].ParameterType);
        }

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
