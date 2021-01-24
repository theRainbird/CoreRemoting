/*
 * Code is copied from https://github.com/zyanfx/SafeDeserializationHelpers
 * Many thanks to yallie for this great extensions to make BinaryFormatter a lot safer.
 */

using System.Diagnostics.CodeAnalysis;

namespace CoreRemoting.Serialization.Binary
{
using System;
    using System.Collections.Generic;

    /// <summary>
    /// Blacklist-based delegate validator.
    /// </summary>
    public class DelegateValidator : IDelegateValidator
    {
        /// <summary>
        /// The default blacklist of the namespaces.
        /// </summary>
        private static readonly string[] DefaultBlacklistedNamespaces = new[]
        {
            "System.IO",
            "System.Diagnostics",
            "System.Management",
            "System.Reflection",
            "System.Configuration",
            "System.Security",
            "System.Web",
            "System.ServiceModel",
            "System.Activities",
            "System.Workflow",
        };

        /// <summary>
        /// Initializes a new instance of the <see cref="DelegateValidator"/> class.
        /// </summary>
        /// <param name="blacklistedNamespaces">Namespace blacklist.</param>
        public DelegateValidator(params string[] blacklistedNamespaces)
        {
            if (blacklistedNamespaces == null || blacklistedNamespaces.Length == 0)
            {
                blacklistedNamespaces = DefaultBlacklistedNamespaces;
            }

            BlacklistedNamespaces = new HashSet<string>(blacklistedNamespaces, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Gets or sets the default <see cref="IDelegateValidator"/> instance.
        /// </summary>
        public static IDelegateValidator Default { get; set; } = new DelegateValidator();

        private HashSet<string> BlacklistedNamespaces { get; }

        /// <summary>
        /// Validates the given delegates.
        /// Throws exceptions for methods defined in the blacklisted namespaces.
        /// </summary>
        /// <param name="del">The delegate to validate.</param>
        [SuppressMessage("ReSharper", "PossibleNullReferenceException")]
        public void ValidateDelegate(Delegate del)
        {
            if (del == null)
            {
                return;
            }

            foreach (var d in del.GetInvocationList())
            {
                if (d == null)
                {
                    continue;
                }

                var type = d.Method.DeclaringType;
                if (BlacklistedNamespaces.Contains(type.Namespace))
                {
                    var msg = $"Deserializing delegates for {type.FullName} may be unsafe.";
                    throw new UnsafeDeserializationException(msg);
                }
            }
        }
    }
}