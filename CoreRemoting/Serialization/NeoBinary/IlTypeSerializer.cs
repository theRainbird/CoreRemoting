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
                // Match NeoBinarySerializer.GetAllFieldsInHierarchy ordering/selection
                var typeFields = currentType.GetFields(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                
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

                // Bind the DynamicMethod to NeoBinarySerializer to allow access to its non-public instance methods
                var dynamicMethod = new DynamicMethod(
                    $"Serialize_{type.Name}_{Guid.NewGuid():N}",
                    typeof(void),
                    new[] { typeof(object), typeof(BinaryWriter), typeof(SerializationContext) },
                    typeof(NeoBinarySerializer),
                    true);

                var il = dynamicMethod.GetILGenerator();
                var writerLocal = il.DeclareLocal(typeof(BinaryWriter));
                var contextLocal = il.DeclareLocal(typeof(SerializationContext));
                var typedObjLocal = il.DeclareLocal(type);

                // Store args to locals
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Stloc, writerLocal);
                il.Emit(OpCodes.Ldarg_2);
                il.Emit(OpCodes.Stloc, contextLocal);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Castclass, type);
                il.Emit(OpCodes.Stloc, typedObjLocal);

                // Write field count to match general serializer format
                il.Emit(OpCodes.Ldloc, writerLocal);
                il.Emit(OpCodes.Ldc_I4, fields.Length);
                il.Emit(OpCodes.Callvirt, typeof(BinaryWriter).GetMethod("Write", new[] { typeof(int) }));

                // For each field: write name and serialize value via NeoBinarySerializer.SerializeObject
                foreach (var field in fields)
                {
                    // writer.Write(fieldName)
                    il.Emit(OpCodes.Ldloc, writerLocal);
                    il.Emit(OpCodes.Ldstr, field.Name);
                    il.Emit(OpCodes.Callvirt, typeof(BinaryWriter).GetMethod("Write", new[] { typeof(string) }));

                    // Load serializer instance from context
                    il.Emit(OpCodes.Ldloc, contextLocal);
                    il.Emit(OpCodes.Callvirt, typeof(SerializationContext).GetProperty("Serializer").GetGetMethod());

                    // arg0: object value
                    il.Emit(OpCodes.Ldloc, typedObjLocal);
                    il.Emit(OpCodes.Ldfld, field);
                    if (field.FieldType.IsValueType)
                    {
                        il.Emit(OpCodes.Box, field.FieldType);
                    }

                    // arg1: BinaryWriter
                    il.Emit(OpCodes.Ldloc, writerLocal);

                    // arg2: HashSet<object> serializedObjects
                    il.Emit(OpCodes.Ldloc, contextLocal);
                    il.Emit(OpCodes.Callvirt, typeof(SerializationContext).GetProperty("SerializedObjects").GetGetMethod());

                    // arg3: Dictionary<object,int> objectMap
                    il.Emit(OpCodes.Ldloc, contextLocal);
                    il.Emit(OpCodes.Callvirt, typeof(SerializationContext).GetProperty("ObjectMap").GetGetMethod());

                    // call NeoBinarySerializer.SerializeObject(object, BinaryWriter, HashSet<object>, Dictionary<object,int>)
                    il.Emit(OpCodes.Callvirt, typeof(NeoBinarySerializer).GetMethod(
                        "SerializeObject",
                        BindingFlags.NonPublic | BindingFlags.Instance));
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

                // Bind the DynamicMethod to NeoBinarySerializer to allow access to its non-public instance methods
                var dynamicMethod = new DynamicMethod(
                    $"Deserialize_{type.Name}_{Guid.NewGuid():N}",
                    typeof(object),
                    new[] { typeof(BinaryReader), typeof(DeserializationContext) },
                    typeof(NeoBinarySerializer),
                    true);

                var il = dynamicMethod.GetILGenerator();
                var readerLocal = il.DeclareLocal(typeof(BinaryReader));
                var contextLocal = il.DeclareLocal(typeof(DeserializationContext));
                var typedObjLocal = il.DeclareLocal(type);
                var valueLocal = il.DeclareLocal(typeof(object));
                var countLocal = il.DeclareLocal(typeof(int));

                // Store args to locals
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Stloc, readerLocal);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Stloc, contextLocal);

                // Create instance
                if (type.IsValueType)
                {
                    il.Emit(OpCodes.Ldloca, typedObjLocal);
                    il.Emit(OpCodes.Initobj, type);
                }
                else
                {
                    // var obj = context.Serializer.CreateInstanceWithoutConstructor(type)
                    il.Emit(OpCodes.Ldloc, contextLocal);
                    il.Emit(OpCodes.Callvirt, typeof(DeserializationContext).GetProperty("Serializer").GetGetMethod());
                    il.Emit(OpCodes.Ldtoken, type);
                    il.Emit(OpCodes.Call, typeof(Type).GetMethod("GetTypeFromHandle"));
                    il.Emit(OpCodes.Callvirt, typeof(NeoBinarySerializer).GetMethod(
                        "CreateInstanceWithoutConstructor",
                        BindingFlags.NonPublic | BindingFlags.Instance));
                    il.Emit(OpCodes.Castclass, type);
                    il.Emit(OpCodes.Stloc, typedObjLocal);
                }

                // Read and ignore field count (kept for format parity and forward-compat)
                il.Emit(OpCodes.Ldloc, readerLocal);
                il.Emit(OpCodes.Call, typeof(BinaryReader).GetMethod("ReadInt32"));
                il.Emit(OpCodes.Stloc, countLocal);

                // For each field, read name, then value, then assign
                foreach (var field in fields)
                {
                    // Read and discard field name
                    il.Emit(OpCodes.Ldloc, readerLocal);
                    il.Emit(OpCodes.Call, typeof(BinaryReader).GetMethod("ReadString"));
                    il.Emit(OpCodes.Pop);

                    // value = context.Serializer.DeserializeObject(reader, deserializedObjects)
                    il.Emit(OpCodes.Ldloc, contextLocal);
                    il.Emit(OpCodes.Callvirt, typeof(DeserializationContext).GetProperty("Serializer").GetGetMethod());
                    il.Emit(OpCodes.Ldloc, readerLocal);
                    il.Emit(OpCodes.Ldloc, contextLocal);
                    il.Emit(OpCodes.Callvirt, typeof(DeserializationContext).GetProperty("DeserializedObjects").GetGetMethod());
                    il.Emit(OpCodes.Callvirt, typeof(NeoBinarySerializer).GetMethod(
                        "DeserializeObject",
                        BindingFlags.NonPublic | BindingFlags.Instance));
                    il.Emit(OpCodes.Stloc, valueLocal);

                    // Assign field
                    if (type.IsValueType)
                    {
                        // Load address of value-type local
                        il.Emit(OpCodes.Ldloca, typedObjLocal);
                    }
                    else
                    {
                        il.Emit(OpCodes.Ldloc, typedObjLocal);
                    }

                    // Load value and cast/unbox to field type
                    il.Emit(OpCodes.Ldloc, valueLocal);
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

                // Return object (box if value type)
                if (type.IsValueType)
                {
                    il.Emit(OpCodes.Ldloc, typedObjLocal);
                    il.Emit(OpCodes.Box, type);
                }
                else
                {
                    il.Emit(OpCodes.Ldloc, typedObjLocal);
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
        }
    }
}