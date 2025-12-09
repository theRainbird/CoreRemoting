using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace CoreRemoting.Serialization.NeoBinary
{
    /// <summary>
    /// High-performance IL-based serializer for complex objects.
    /// Generates optimized IL code at runtime for serialization and deserialization.
    /// </summary>
    public class IlTypeSerializer
    {
        private readonly ConcurrentDictionary<Type, ObjectSerializerDelegate> _serializers = new();
        private readonly ConcurrentDictionary<Type, ObjectDeserializerDelegate> _deserializers = new();
        private readonly ConcurrentDictionary<Type, FieldInfo[]> _fieldCache = new();
        private readonly ConcurrentDictionary<string, string> _stringPool = new();

        /// <summary>
        /// Delegate for object serialization.
        /// </summary>
        /// <param name="obj">Object to serialize</param>
        /// <param name="writer">Binary writer</param>
        /// <param name="context">Serialization context</param>
        public delegate void ObjectSerializerDelegate(object obj, BinaryWriter writer, SerializationContext context);

        /// <summary>
        /// Delegate for object deserialization.
        /// </summary>
        /// <param name="reader">Binary reader</param>
        /// <param name="context">Deserialization context</param>
        /// <returns>Deserialized object</returns>
        public delegate object ObjectDeserializerDelegate(BinaryReader reader, DeserializationContext context);

        /// <summary>
        /// Serialization context containing shared state.
        /// </summary>
        public class SerializationContext
        {
            public HashSet<object> SerializedObjects { get; set; }
            public Dictionary<object, int> ObjectMap { get; set; }
            public NeoBinarySerializer Serializer { get; set; }
            public ConcurrentDictionary<string, string> StringPool { get; set; }
        }

        /// <summary>
        /// Deserialization context containing shared state.
        /// </summary>
        public class DeserializationContext
        {
            public Dictionary<int, object> DeserializedObjects { get; set; }
            public NeoBinarySerializer Serializer { get; set; }
        }

        /// <summary>
        /// Gets or creates a serializer delegate for the specified type.
        /// </summary>
        /// <param name="type">Type to serialize</param>
        /// <returns>Serializer delegate</returns>
        public ObjectSerializerDelegate GetSerializer(Type type)
        {
            return _serializers.GetOrAdd(type, CreateSerializer);
        }

        /// <summary>
        /// Gets or creates a deserializer delegate for the specified type.
        /// </summary>
        /// <param name="type">Type to deserialize</param>
        /// <returns>Deserializer delegate</returns>
        public ObjectDeserializerDelegate GetDeserializer(Type type)
        {
            return _deserializers.GetOrAdd(type, CreateDeserializer);
        }

        /// <summary>
        /// Creates an optimized serializer delegate using IL generation.
        /// </summary>
        /// <param name="type">Type to create serializer for</param>
        /// <returns>Serializer delegate</returns>
        private ObjectSerializerDelegate CreateSerializer(Type type)
        {
            var fields = GetAllFieldsInHierarchy(type);
            _fieldCache[type] = fields;

            var dynamicMethod = new DynamicMethod(
                $"Serialize_{type.Name}_{Guid.NewGuid():N}",
                typeof(void),
                new[] { typeof(object), typeof(BinaryWriter), typeof(IlTypeSerializer.SerializationContext) },
                type,
                true);

            var il = dynamicMethod.GetILGenerator();
            var objLocal = il.DeclareLocal(type);
            var contextLocal = il.DeclareLocal(typeof(SerializationContext));

            // Cast object to specific type
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, type);
            il.Emit(OpCodes.Stloc, objLocal);

            // Store context in local
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Stloc, contextLocal);

            // Write field count
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldc_I4, fields.Length);
            il.Emit(OpCodes.Call, typeof(BinaryWriter).GetMethod("Write", new[] { typeof(int) }));

            // Generate IL for each field
            foreach (var field in fields)
            {
                // Write field name (pooled)
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldstr, _stringPool.GetOrAdd(field.Name, n => n));
                il.Emit(OpCodes.Call, typeof(BinaryWriter).GetMethod("Write", new[] { typeof(string) }));

                // Get field value
                il.Emit(OpCodes.Ldloc, objLocal);
                il.Emit(OpCodes.Ldfld, field);

                // Box if value type
                if (field.FieldType.IsValueType)
                {
                    il.Emit(OpCodes.Box, field.FieldType);
                }

                // Call SerializeObject method
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldloc, contextLocal);
                il.Emit(OpCodes.Ldfld, typeof(SerializationContext).GetField("SerializedObjects"));
                il.Emit(OpCodes.Ldloc, contextLocal);
                il.Emit(OpCodes.Ldfld, typeof(SerializationContext).GetField("ObjectMap"));
                il.Emit(OpCodes.Ldloc, contextLocal);
                il.Emit(OpCodes.Ldfld, typeof(SerializationContext).GetField("Serializer"));

                // Call NeoBinarySerializer.SerializeObject
                il.Emit(OpCodes.Callvirt, typeof(NeoBinarySerializer).GetMethod(
                    "SerializeObject",
                    BindingFlags.NonPublic | BindingFlags.Instance));

                // Pop the void result
                il.Emit(OpCodes.Pop);
            }

            il.Emit(OpCodes.Ret);
            return (ObjectSerializerDelegate)dynamicMethod.CreateDelegate(typeof(ObjectSerializerDelegate));
        }

        /// <summary>
        /// Creates an optimized deserializer delegate using IL generation.
        /// </summary>
        /// <param name="type">Type to create deserializer for</param>
        /// <returns>Deserializer delegate</returns>
        private ObjectDeserializerDelegate CreateDeserializer(Type type)
        {
            var fields = _fieldCache.GetOrAdd(type, GetAllFieldsInHierarchy);

            var dynamicMethod = new DynamicMethod(
                $"Deserialize_{type.Name}_{Guid.NewGuid():N}",
                typeof(object),
                new[] { typeof(BinaryReader), typeof(IlTypeSerializer.DeserializationContext) },
                type,
                true);

            var il = dynamicMethod.GetILGenerator();
            var objLocal = il.DeclareLocal(type);
            var contextLocal = il.DeclareLocal(typeof(DeserializationContext));

            // Store context in local
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Stloc, contextLocal);

            // Create instance
            if (type.IsValueType)
            {
                // For value types, create default value and box it
                il.Emit(OpCodes.Ldloca, objLocal);
                il.Emit(OpCodes.Initobj, type);
                il.Emit(OpCodes.Ldloc, objLocal);
                il.Emit(OpCodes.Box, type);
            }
            else
            {
                // For reference types, create instance without constructor
                il.Emit(OpCodes.Ldtoken, type);
                il.Emit(OpCodes.Call, typeof(Type).GetMethod("GetTypeFromHandle"));
                il.Emit(OpCodes.Call, typeof(NeoBinarySerializer).GetMethod(
                    "CreateInstanceWithoutConstructor",
                    BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance));
                il.Emit(OpCodes.Castclass, type);
                il.Emit(OpCodes.Stloc, objLocal);

                // Register object for circular references
                il.Emit(OpCodes.Ldloc, contextLocal);
                il.Emit(OpCodes.Ldfld, typeof(DeserializationContext).GetField("DeserializedObjects"));
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Call, typeof(BinaryReader).GetMethod("ReadInt32"));
                il.Emit(OpCodes.Ldloc, objLocal);
                il.Emit(OpCodes.Box, type);
                il.Emit(OpCodes.Callvirt, typeof(Dictionary<int, object>).GetMethod("set_Item"));
            }

            // Read field count (but don't use it - we know the field count)
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, typeof(BinaryReader).GetMethod("ReadInt32"));
            il.Emit(OpCodes.Pop);

            // Generate IL for each field
            foreach (var field in fields)
            {
                // Validate field type to prevent null reference errors
                if (field.FieldType == null)
                {
                    throw new ArgumentNullException(nameof(field.FieldType),
                        $"Field '{field.Name}' on type '{field.DeclaringType?.Name}' has null FieldType.");
                }

                // Read field name (but don't use it - we read in order)
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Call, typeof(BinaryReader).GetMethod("ReadString"));
                il.Emit(OpCodes.Pop);

                // Deserialize field value
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldloc, contextLocal);
                il.Emit(OpCodes.Ldfld, typeof(DeserializationContext).GetField("DeserializedObjects"));
                il.Emit(OpCodes.Ldloc, contextLocal);
                il.Emit(OpCodes.Ldfld, typeof(DeserializationContext).GetField("Serializer"));

                // Call NeoBinarySerializer.DeserializeObject
                il.Emit(OpCodes.Callvirt, typeof(NeoBinarySerializer).GetMethod(
                    "DeserializeObject",
                    BindingFlags.NonPublic | BindingFlags.Instance));

                // Set field value
                if (type.IsValueType)
                {
                    // For value types, we need to work with the boxed instance
                    // Create a local for the deserialized value
                    var valueLocal = il.DeclareLocal(field.FieldType);
                    
                    // Cast and unbox the deserialized value to the correct field type
                    if (field.FieldType.IsValueType)
                    {
                        il.Emit(OpCodes.Unbox_Any, field.FieldType);
                    }
                    else
                    {
                        il.Emit(OpCodes.Castclass, field.FieldType);
                    }
                    
                    // Store the deserialized value in our local
                    il.Emit(OpCodes.Stloc, valueLocal);
                    
                    // Load the address of the struct local
                    il.Emit(OpCodes.Ldloca, objLocal);
                    
                    // Load the field value from our value local
                    il.Emit(OpCodes.Ldloc, valueLocal);
                    
                    // Store the value into the struct field
                    il.Emit(OpCodes.Stfld, field);
                }
                else
                {
                    // For reference types, set field directly
                    il.Emit(OpCodes.Ldloc, objLocal);
                    
                    // Cast to field type if necessary
                    if (field.FieldType.IsValueType)
                    {
                        il.Emit(OpCodes.Unbox_Any, field.FieldType);
                    }
                    else
                    {
                        il.Emit(OpCodes.Castclass, field.FieldType);
                    }

                    il.Emit(OpCodes.Stfld, field);
                }
            }

            // Return the object
            if (type.IsValueType)
            {
                il.Emit(OpCodes.Ldloc, objLocal);
                il.Emit(OpCodes.Box, type);
            }
            else
            {
                il.Emit(OpCodes.Ldloc, objLocal);
            }

            il.Emit(OpCodes.Ret);
            return (ObjectDeserializerDelegate)dynamicMethod.CreateDelegate(typeof(ObjectDeserializerDelegate));
        }

        /// <summary>
        /// Gets all fields in the type hierarchy, including private fields from base types.
        /// </summary>
        /// <param name="type">Type to get fields for</param>
        /// <returns>Array of field information</returns>
        private FieldInfo[] GetAllFieldsInHierarchy(Type type)
        {
            var fields = new List<FieldInfo>();
            var currentType = type;

            while (currentType != null && currentType != typeof(object))
            {
                var typeFields = currentType.GetFields(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);

                // Filter out compiler-generated fields and static fields
                foreach (var field in typeFields)
                {
                    if (!field.IsStatic && !IsCompilerGenerated(field))
                    {
                        fields.Add(field);
                    }
                }

                currentType = currentType.BaseType;
            }

            return fields.ToArray();
        }



        /// <summary>
        /// Checks if a field is compiler-generated.
        /// </summary>
        /// <param name="field">Field to check</param>
        /// <returns>True if field is compiler-generated</returns>
        private static bool IsCompilerGenerated(FieldInfo field)
        {
            return field.IsDefined(typeof(CompilerGeneratedAttribute), false) ||
                   field.Name.StartsWith("<") && field.Name.Contains(">");
        }

        /// <summary>
        /// Clears all cached serializers and deserializers.
        /// </summary>
        public void ClearCache()
        {
            _serializers.Clear();
            _deserializers.Clear();
            _fieldCache.Clear();
        }

        /// <summary>
        /// Gets cache statistics.
        /// </summary>
        /// <returns>Cache statistics</returns>
        public (int SerializerCount, int DeserializerCount, int FieldCacheCount) GetCacheStats()
        {
            return (_serializers.Count, _deserializers.Count, _fieldCache.Count);
        }
    }
}