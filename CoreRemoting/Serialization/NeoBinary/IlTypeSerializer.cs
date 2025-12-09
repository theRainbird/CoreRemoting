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
        /// Gets all fields in the type hierarchy, including private fields from base types.
        /// </summary>
        /// <param name="type">Type to get fields for</param>
        /// <returns>Array of all fields</returns>
        private FieldInfo[] GetAllFields(Type type)
        {
            var fields = new List<FieldInfo>();
            var currentType = type;

            while (currentType != null && currentType != typeof(object))
            {
                var typeFields = currentType.GetFields(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy);
                
                fields.AddRange(typeFields);
                currentType = currentType.BaseType;
            }

            return fields.ToArray();
        }

        /// <summary>
        /// Creates a serializer delegate for the specified type.
        /// </summary>
        /// <param name="type">Type to create serializer for</param>
        /// <returns>Serializer delegate</returns>
        public ObjectSerializerDelegate CreateSerializer(Type type)
        {
            return _serializers.GetOrAdd(type, t =>
            {
                var fields = _fieldCache.GetOrAdd(type, GetAllFields);

                var dynamicMethod = new DynamicMethod(
                    $"Serialize_{type.Name}_{Guid.NewGuid():N}",
                    typeof(void),
                    new[] { typeof(object), typeof(BinaryWriter), typeof(SerializationContext) });

                var il = dynamicMethod.GetILGenerator();
                var writerLocal = il.DeclareLocal(typeof(BinaryWriter));
                var contextLocal = il.DeclareLocal(typeof(SerializationContext));

                // Load arguments
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Stloc, writerLocal);
                il.Emit(OpCodes.Ldarg_2);
                il.Emit(OpCodes.Stloc, contextLocal);

                // Generate IL for each field
                foreach (var field in fields)
                {
                    // Write field name
                    il.Emit(OpCodes.Ldloc, writerLocal);
                    il.Emit(OpCodes.Ldstr, field.Name);
                    il.Emit(OpCodes.Callvirt, typeof(BinaryWriter).GetMethod("Write", new[] { typeof(string) }));

                    // Get field value
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ldfld, field);

                    // Call SerializeObject method
                    il.Emit(OpCodes.Ldloc, writerLocal);
                    il.Emit(OpCodes.Ldloc, contextLocal);
                    il.Emit(OpCodes.Callvirt, typeof(SerializationContext).GetProperty("SerializedObjects").GetGetMethod());
                    il.Emit(OpCodes.Ldloc, contextLocal);
                    il.Emit(OpCodes.Callvirt, typeof(SerializationContext).GetProperty("ObjectMap").GetGetMethod());
                    il.Emit(OpCodes.Ldloc, contextLocal);
                    il.Emit(OpCodes.Callvirt, typeof(SerializationContext).GetProperty("Serializer").GetGetMethod());

                    il.Emit(OpCodes.Callvirt, typeof(NeoBinarySerializer).GetMethod(
                        "SerializeObject",
                        BindingFlags.NonPublic | BindingFlags.Instance));

                    il.Emit(OpCodes.Pop);
                }

                il.Emit(OpCodes.Ret);
                return (ObjectSerializerDelegate)dynamicMethod.CreateDelegate(typeof(ObjectSerializerDelegate));
            });
        }

        /// <summary>
        /// Creates a deserializer delegate for the specified type.
        /// </summary>
        /// <param name="type">Type to create deserializer for</param>
        /// <returns>Deserializer delegate</returns>
        public ObjectDeserializerDelegate CreateDeserializer(Type type)
        {
            return _deserializers.GetOrAdd(type, t =>
            {
                var fields = _fieldCache.GetOrAdd(type, GetAllFields);

                var dynamicMethod = new DynamicMethod(
                    $"Deserialize_{type.Name}_{Guid.NewGuid():N}",
                    typeof(object),
                    new[] { typeof(BinaryReader), typeof(DeserializationContext) });

                var il = dynamicMethod.GetILGenerator();
                var readerLocal = il.DeclareLocal(typeof(BinaryReader));
                var contextLocal = il.DeclareLocal(typeof(DeserializationContext));
                var objLocal = il.DeclareLocal(typeof(object));

                // Load arguments
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Stloc, readerLocal);
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
                }

                // Register object for circular references
                il.Emit(OpCodes.Ldloc, contextLocal);
                il.Emit(OpCodes.Callvirt, typeof(DeserializationContext).GetProperty("DeserializedObjects").GetGetMethod());
                il.Emit(OpCodes.Ldloc, readerLocal);
                il.Emit(OpCodes.Call, typeof(BinaryReader).GetMethod("ReadInt32"));
                il.Emit(OpCodes.Ldloc, objLocal);
                il.Emit(OpCodes.Box, type);
                il.Emit(OpCodes.Callvirt, typeof(Dictionary<int, object>).GetMethod("set_Item"));

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
                    il.Emit(OpCodes.Ldloc, readerLocal);
                    il.Emit(OpCodes.Call, typeof(BinaryReader).GetMethod("ReadString"));
                    il.Emit(OpCodes.Pop);

                    // Deserialize field value
                    il.Emit(OpCodes.Ldloc, readerLocal);
                    il.Emit(OpCodes.Ldloc, contextLocal);
                    il.Emit(OpCodes.Callvirt, typeof(DeserializationContext).GetProperty("DeserializedObjects").GetGetMethod());
                    il.Emit(OpCodes.Ldloc, contextLocal);
                    il.Emit(OpCodes.Callvirt, typeof(DeserializationContext).GetProperty("Serializer").GetGetMethod());

                    // Call NeoBinarySerializer.DeserializeObject
                    il.Emit(OpCodes.Callvirt, typeof(NeoBinarySerializer).GetMethod(
                        "DeserializeObject",
                        BindingFlags.NonPublic | BindingFlags.Instance));

                    // Set field value
                    if (type.IsValueType)
                    {
                        // For value types, temporarily skip to avoid IL complexity
                        // Fall back to reflection-based deserialization
                        throw new NotSupportedException($"IL-based deserialization for value types is not yet implemented: {type.Name}");
                    }
                    else
                    {
                        // For reference types, set field directly
                        il.Emit(OpCodes.Ldloc, objLocal);
                        
                        // Cast to field type if necessary
                        if (field.FieldType.IsValueType)
                        {
                            il.Emit(OpCodes.Unbox, field.FieldType);
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
            });
        }

        /// <summary>
        /// Clears all cached serializers and deserializers.
        /// </summary>
        public void ClearCache()
        {
            _serializers.Clear();
            _deserializers.Clear();
            _fieldCache.Clear();
            _stringPool.Clear();
        }

        /// <summary>
        /// Cache statistics information.
        /// </summary>
        public class CacheStatistics
        {
            public int SerializerCount { get; set; }
            public int DeserializerCount { get; set; }
            public int FieldCacheCount { get; set; }
            public int StringPoolCount { get; set; }
            public long TotalSerializations { get; set; }
            public long TotalDeserializations { get; set; }
            public long CacheHits { get; set; }
            public long CacheMisses { get; set; }
            public double HitRatio => CacheHits + CacheMisses > 0 ? (double)CacheHits / (CacheHits + CacheMisses) : 0;
        }

        /// <summary>
        /// Cached serializer with statistics.
        /// </summary>
        private class CachedSerializer
        {
            public ObjectSerializerDelegate Serializer { get; set; }
            public DateTime CreatedAt { get; set; }
            public DateTime LastAccessed { get; set; }
            public long AccessCount { get; set; }
            public long SerializationCount { get; set; }
            public long TotalSerializationTimeTicks { get; set; }

            public void RecordAccess()
            {
                LastAccessed = DateTime.UtcNow;
                AccessCount++;
            }

            public void RecordSerialization(long ticks)
            {
                SerializationCount++;
                TotalSerializationTimeTicks += ticks;
            }

            public double AverageSerializationTime => SerializationCount > 0 
                ? (double)TotalSerializationTimeTicks / SerializationCount / TimeSpan.TicksPerMillisecond 
                : 0;
        }

        /// <summary>
        /// Cached deserializer with statistics.
        /// </summary>
        private class CachedDeserializer
        {
            public ObjectDeserializerDelegate Deserializer { get; set; }
            public DateTime CreatedAt { get; set; }
            public DateTime LastAccessed { get; set; }
            public long AccessCount { get; set; }
            public long DeserializationCount { get; set; }
            public long TotalDeserializationTimeTicks { get; set; }

            public void RecordAccess()
            {
                LastAccessed = DateTime.UtcNow;
                AccessCount++;
            }

            public void RecordDeserialization(long ticks)
            {
                DeserializationCount++;
                TotalDeserializationTimeTicks += ticks;
            }

            public double AverageDeserializationTime => DeserializationCount > 0 
                ? (double)TotalDeserializationTimeTicks / DeserializationCount / TimeSpan.TicksPerMillisecond 
                : 0;
        }
    }
}