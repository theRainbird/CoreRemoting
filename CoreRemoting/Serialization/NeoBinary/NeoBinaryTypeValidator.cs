using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace CoreRemoting.Serialization.NeoBinary
{
    /// <summary>
    /// Validates types for secure deserialization in NeoBinary serializer.
    /// </summary>
    public class NeoBinaryTypeValidator
    {
        private readonly HashSet<Type> _allowedTypes;
        private readonly HashSet<Type> _blockedTypes;
        private readonly HashSet<string> _allowedNamespaces;
        private readonly HashSet<string> _blockedNamespaces;
        private readonly HashSet<string> _blockedTypeNames;

        /// <summary>
        /// Creates a new instance of the NeoBinaryTypeValidator class.
        /// </summary>
        public NeoBinaryTypeValidator()
        {
            _allowedTypes = new HashSet<Type>();
            _blockedTypes = new HashSet<Type>();
            _allowedNamespaces = new HashSet<string>();
            _blockedNamespaces = new HashSet<string>();
            _blockedTypeNames = new HashSet<string>();

            // Add default blocked types for security
            AddDefaultBlockedTypes();
        }

        /// <summary>
        /// Gets or sets whether unknown types should be allowed.
        /// </summary>
        public bool AllowUnknownTypes { get; set; } = false;

        /// <summary>
        /// Gets or sets whether strict type checking is enabled.
        /// </summary>
        public bool StrictTypeChecking { get; set; } = true;

        /// <summary>
        /// Gets or sets whether to allow delegates during deserialization.
        /// </summary>
        public bool AllowDelegates { get; set; } = false;

        /// <summary>
        /// Gets or sets whether to allow types from dynamic assemblies.
        /// </summary>
        public bool AllowDynamicAssemblies { get; set; } = false;

        /// <summary>
        /// Validates a type for secure deserialization.
        /// </summary>
        /// <param name="type">Type to validate</param>
        /// <exception cref="NeoBinaryUnsafeDeserializationException">Thrown when type is not allowed</exception>
        public void ValidateType(Type type)
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type));

            // Check if type is explicitly blocked
            if (_blockedTypes.Contains(type))
            {
                throw new NeoBinaryUnsafeDeserializationException($"Type '{type.FullName}' is explicitly blocked.");
            }

            // Check if type is in blocked namespace
            if (IsInBlockedNamespace(type))
            {
                throw new NeoBinaryUnsafeDeserializationException($"Type '{type.FullName}' is in blocked namespace.");
            }

            // Check if type name is blocked
            if (_blockedTypeNames.Contains(type.Name))
            {
                throw new NeoBinaryUnsafeDeserializationException($"Type name '{type.Name}' is blocked.");
            }

            // Check delegate restrictions
            if (!AllowDelegates && typeof(Delegate).IsAssignableFrom(type))
            {
                throw new NeoBinaryUnsafeDeserializationException($"Delegate type '{type.FullName}' is not allowed.");
            }

            // Check dynamic assembly restrictions
            if (!AllowDynamicAssemblies && type.Assembly.IsDynamic)
            {
                throw new NeoBinaryUnsafeDeserializationException($"Type '{type.FullName}' from dynamic assembly is not allowed.");
            }

            // Strict type checking
            if (StrictTypeChecking)
            {
                PerformStrictTypeChecks(type);
            }

            // Check if type is explicitly allowed
            if (_allowedTypes.Contains(type))
                return;

            // Check if type is in allowed namespace
            if (IsInAllowedNamespace(type))
                return;

            // If no explicit allow rules exist, check AllowUnknownTypes
            if (_allowedTypes.Count == 0 && _allowedNamespaces.Count == 0)
            {
                if (!AllowUnknownTypes)
                {
                    throw new NeoBinaryUnsafeDeserializationException($"Unknown type '{type.FullName}' is not allowed. Set AllowUnknownTypes=true or add explicit allow rules.");
                }
                return;
            }

            // If explicit allow rules exist, check AllowUnknownTypes as fallback
            if (AllowUnknownTypes)
                return;

            // Type is not allowed
            throw new NeoBinaryUnsafeDeserializationException($"Type '{type.FullName}' is not allowed.");
        }

        /// <summary>
        /// Adds a type to the allowed types list.
        /// </summary>
        /// <typeparam name="T">Type to allow</typeparam>
        public void AllowType<T>()
        {
            _allowedTypes.Add(typeof(T));
        }

        /// <summary>
        /// Adds a type to the allowed types list.
        /// </summary>
        /// <param name="type">Type to allow</param>
        public void AllowType(Type type)
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type));
            _allowedTypes.Add(type);
        }

        /// <summary>
        /// Adds a type to the allowed types list.
        /// </summary>
        /// <typeparam name="T">Type to allow</typeparam>
        public void AllowGenericType<T>()
        {
            AllowType(typeof(T));
        }

        /// <summary>
        /// Adds a type to the blocked types list.
        /// </summary>
        /// <typeparam name="T">Type to block</typeparam>
        public void BlockType<T>()
        {
            _blockedTypes.Add(typeof(T));
        }

        /// <summary>
        /// Adds a type to the blocked types list.
        /// </summary>
        /// <param name="type">Type to block</param>
        public void BlockType(Type type)
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type));
            _blockedTypes.Add(type);
        }

        /// <summary>
        /// Adds a namespace to the allowed namespaces list.
        /// </summary>
        /// <param name="namespace">Namespace to allow</param>
        public void AllowNamespace(string @namespace)
        {
            if (string.IsNullOrEmpty(@namespace))
                throw new ArgumentException("Namespace cannot be null or empty", nameof(@namespace));
            _allowedNamespaces.Add(@namespace);
        }

        /// <summary>
        /// Adds a namespace to the blocked namespaces list.
        /// </summary>
        /// <param name="namespace">Namespace to block</param>
        public void BlockNamespace(string @namespace)
        {
            if (string.IsNullOrEmpty(@namespace))
                throw new ArgumentException("Namespace cannot be null or empty", nameof(@namespace));
            _blockedNamespaces.Add(@namespace);
        }

        /// <summary>
        /// Adds a type name to the blocked type names list.
        /// </summary>
        /// <param name="typeName">Type name to block</param>
        public void BlockTypeName(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
                throw new ArgumentException("Type name cannot be null or empty", nameof(typeName));
            _blockedTypeNames.Add(typeName);
        }

        /// <summary>
        /// Removes a type from the allowed types list.
        /// </summary>
        /// <typeparam name="T">Type to remove from allowed list</typeparam>
        public void RemoveAllowedType<T>()
        {
            _allowedTypes.Remove(typeof(T));
        }

        /// <summary>
        /// Removes a type from the blocked types list.
        /// </summary>
        /// <typeparam name="T">Type to remove from blocked list</typeparam>
        public void RemoveBlockedType<T>()
        {
            _blockedTypes.Remove(typeof(T));
        }

        /// <summary>
        /// Checks if a type is allowed according to the current validation rules.
        /// </summary>
        /// <param name="type">Type to check</param>
        /// <returns>True if the type is allowed, false otherwise</returns>
        public bool IsTypeAllowed(Type type)
        {
            try
            {
                ValidateType(type);
                return true;
            }
            catch (NeoBinaryUnsafeDeserializationException)
            {
                return false;
            }
        }

        private void AddDefaultBlockedTypes()
        {
            // Block known dangerous types
            BlockTypeName("PSObject");
            BlockTypeName("ActivitySurrogateSelector");
            BlockTypeName("ObjectSurrogate");
            BlockTypeName("ObjectSerializedRef");
            BlockTypeName("DelegateSerializationHolder");
            BlockTypeName("SortedSet`1");
            BlockTypeName("TypeEqualityBinder");

            // Block dangerous namespaces
            BlockNamespace("System.Management.Automation");
            BlockNamespace("System.Workflow.ComponentModel");
            BlockNamespace("System.Web");
            BlockNamespace("System.Windows.Forms");
            BlockNamespace("Microsoft.Win32");
        }

        private bool IsInAllowedNamespace(Type type)
        {
            var typeNamespace = type.Namespace ?? string.Empty;
            return _allowedNamespaces.Any(allowedNs => typeNamespace.StartsWith(allowedNs, StringComparison.Ordinal));
        }

        private bool IsInBlockedNamespace(Type type)
        {
            var typeNamespace = type.Namespace ?? string.Empty;
            return _blockedNamespaces.Any(blockedNs => typeNamespace.StartsWith(blockedNs, StringComparison.Ordinal));
        }

        private void PerformStrictTypeChecks(Type type)
        {
            // Check for potentially dangerous attributes
            var dangerousAttributes = new[]
            {
                "System.SerializableAttribute",
                "System.Runtime.Serialization.DataContractAttribute",
                "System.Xml.Serialization.XmlIncludeAttribute"
            };

            foreach (var attributeName in dangerousAttributes)
            {
                var attributeType = Type.GetType(attributeName);
                if (attributeType != null && type.GetCustomAttributes(attributeType, false).Length > 0)
                {
                    // Additional check for dangerous attribute combinations
                    if (IsDangerousTypeCombination(type))
                    {
                        throw new NeoBinaryUnsafeDeserializationException($"Type '{type.FullName}' has potentially dangerous attribute combination.");
                    }
                }
            }

            // Check for types that implement dangerous interfaces
            var dangerousInterfaces = new[]
            {
                "System.Runtime.Serialization.ISerializable",
                "System.Runtime.Serialization.IDeserializationCallback",
                "System.Runtime.Serialization.IObjectReference"
            };

            foreach (var interfaceName in dangerousInterfaces)
            {
                var interfaceType = Type.GetType(interfaceName);
                if (interfaceType != null && interfaceType.IsAssignableFrom(type))
                {
                    // Allow ISerializable only for known safe types
                    if (interfaceName == "System.Runtime.Serialization.ISerializable" && !IsKnownSafeISerializableType(type))
                    {
                        throw new NeoBinaryUnsafeDeserializationException($"Type '{type.FullName}' implements dangerous interface '{interfaceName}'.");
                    }
                }
            }
        }

        private bool IsDangerousTypeCombination(Type type)
        {
            // Check for combinations that could be exploited
            var hasSerializableAttribute = type.GetCustomAttributes(typeof(SerializableAttribute), false).Length > 0;
            var HasISerializable = typeof(System.Runtime.Serialization.ISerializable).IsAssignableFrom(type);
            var HasDeserializationCallback = typeof(System.Runtime.Serialization.IDeserializationCallback).IsAssignableFrom(type);

            // Known safe types are never dangerous
            if (IsKnownSafeISerializableType(type))
                return false;

            // Decimal is safe - it's a primitive type
            if (type == typeof(decimal))
                return false;

            return hasSerializableAttribute && (HasISerializable || HasDeserializationCallback);
        }

        private bool IsKnownSafeISerializableType(Type type)
        {
            // List of known safe ISerializable types
            var safeTypes = new[]
            {
                typeof(string),
                typeof(DateTime),
                typeof(DateTimeOffset),
                typeof(TimeSpan),
                typeof(Guid),
                typeof(Uri),
                typeof(System.Version),
                typeof(System.Text.StringBuilder),
                typeof(System.Collections.BitArray),
                typeof(System.Collections.Hashtable),
                typeof(System.Collections.ArrayList),
                typeof(System.Collections.Queue),
                typeof(System.Collections.Stack),
                typeof(System.Collections.SortedList)
            };

            return safeTypes.Contains(type) || 
                   type.Namespace?.StartsWith("System", StringComparison.Ordinal) == true;
        }
    }
}