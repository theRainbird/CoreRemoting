using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace CoreRemoting.Serialization.NeoBinary
{
    /// <summary>
    /// High-performance IL-based serializer for complex objects.
    /// Generates optimized IL code at runtime for serialization and deserialization.
    /// </summary>
    public class IlTypeSerializer
    {
        /// <summary>
        /// Subformat tag indicating the compact IL layout (no field names/count).
        /// Written immediately after type info for complex objects.
        /// </summary>
        public const byte CompactLayoutTag = 0xFE;

        private readonly ConcurrentDictionary<Type, ObjectSerializerDelegate> _serializers = new();
        private readonly ConcurrentDictionary<Type, ObjectDeserializerDelegate> _deserializers = new();
        private readonly ConcurrentDictionary<Type, ObjectSerializerDelegate> _compactSerializers = new();
        private readonly ConcurrentDictionary<Type, ObjectDeserializerDelegate> _compactDeserializers = new();
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
            /// <summary>
            /// A collection of objects that have already been serialized in the current context.
            /// Used to avoid duplicate serialization of the same object.
            /// </summary>
            public HashSet<object> SerializedObjects { get; set; }

            /// <summary>
            /// A dictionary mapping objects to their unique identifiers in the current serialization context.
            /// Used to track and manage object references during serialization and deserialization processes.
            /// </summary>
            public Dictionary<object, int> ObjectMap { get; set; }

            /// <summary>
            /// The main serializer class used for serializing and deserializing objects.
            /// This serializer leverages high-performance IL-based serialization techniques to enhance security and performance compared to traditional binary formatters.
            /// It manages a serialization context which includes tracking of already serialized objects to avoid duplication and a string pool for efficient string handling.
            /// </summary>
            public NeoBinarySerializer Serializer { get; set; }

            /// <summary>
            /// A pool of reusable string instances to optimize memory usage and improve performance.
            /// The string pool helps in reducing the memory footprint by reusing string objects that are likely to be duplicated throughout the application.
            /// </summary>
            public ConcurrentDictionary<string, string> StringPool { get; set; }
        }

        /// <summary>
        /// Deserialization context containing shared state.
        /// </summary>
        public class DeserializationContext
        {
            /// <summary>
            /// A dictionary mapping object IDs to deserialized objects.
            /// Used during deserialization to maintain a cache of already created objects,
            /// preventing the creation of duplicate instances and handling forward references.
            /// </summary>
            public Dictionary<int, object> DeserializedObjects { get; set; }

            /// <summary>
            /// Represents the core serializer used for serialization and deserialization processes.
            /// This serializer integrates with an IlTypeSerializer.DeserializationContext to manage
            /// serialized objects, forward references, and deserialized objects during complex data structures handling.
            /// </summary>
            public NeoBinarySerializer Serializer { get; set; }

            /// <summary>
            /// A list of forward references encountered during deserialization.
            /// Forward references occur when an object is serialized before all of its fields are resolved. This property stores these references to allow proper resolution later.
            /// </summary>
            public List<(object targetObject, FieldInfo field, int placeholderObjectId)> ForwardReferences { get; set; } = new();
            
            /// <summary>
            /// A dictionary used for object-to-ID mapping during deserialization.
            /// Maps objects to their corresponding IDs in the deserialized context. This
            /// is crucial for handling self-references and resolving forward references
            /// efficiently.
            /// </summary>
            public Dictionary<object, int> ObjectToIdMap { get; set; } = new();
        }

        /// <summary>
        /// Gets all fields in the type hierarchy, including private fields from base types.
        /// </summary>
        /// <param name="type">Type to get fields for</param>
        /// <returns>Array of all fields</returns>
        private FieldInfo[] GetAllFields(Type type)
        {
            // Skip IL generation for reflection types to avoid IL generation issues
            if (typeof(MemberInfo).IsAssignableFrom(type) ||
                typeof(ParameterInfo).IsAssignableFrom(type) ||
                typeof(Module).IsAssignableFrom(type) ||
                typeof(Assembly).IsAssignableFrom(type) ||
                typeof(System.Reflection.AssemblyName).IsAssignableFrom(type))
            {
                return new FieldInfo[0]; // No field serialization for reflection types
            }

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
                if (type.IsValueType)
                {
                    // Unbox the boxed struct to the value-type local
                    il.Emit(OpCodes.Unbox_Any, type);
                }
                else
                {
                    il.Emit(OpCodes.Castclass, type);
                }
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
                    if (type.IsValueType)
                    {
                        // For value-type container, load address before Ldfld
                        il.Emit(OpCodes.Ldloca, typedObjLocal);
                    }
                    else
                    {
                        il.Emit(OpCodes.Ldloc, typedObjLocal);
                    }
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
        /// Creates a compact-layout serializer delegate for the specified type (no field names/count).
        /// </summary>
        public ObjectSerializerDelegate CreateCompactSerializer(Type type)
        {
            return _compactSerializers.GetOrAdd(type, t =>
            {
                var fields = _fieldCache.GetOrAdd(type, GetAllFields);

                var dynamicMethod = new DynamicMethod(
                    $"SerializeCompact_{type.Name}_{Guid.NewGuid():N}",
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
                if (type.IsValueType)
                {
                    il.Emit(OpCodes.Unbox_Any, type);
                }
                else
                {
                    il.Emit(OpCodes.Castclass, type);
                }
                il.Emit(OpCodes.Stloc, typedObjLocal);

                // For each field: directly serialize value (no field names/count)
                foreach (var field in fields)
                {
                    // Load serializer instance from context
                    il.Emit(OpCodes.Ldloc, contextLocal);
                    il.Emit(OpCodes.Callvirt, typeof(SerializationContext).GetProperty("Serializer").GetGetMethod());

                    // arg0: object value
                    if (type.IsValueType)
                    {
                        il.Emit(OpCodes.Ldloca, typedObjLocal);
                    }
                    else
                    {
                        il.Emit(OpCodes.Ldloc, typedObjLocal);
                    }
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
        /// Creates a deserializer delegate for specified type.
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
                    // Label to jump to end of this field handling when special-case path is taken
                    var endOfFieldLabel = il.DefineLabel();

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

                    // Check if value is ForwardReferencePlaceholder
                    var isPlaceholderLabel = il.DefineLabel();
                    var afterPlaceholderCheckLabel = il.DefineLabel();
                    var placeholderLocal = il.DeclareLocal(typeof(NeoBinarySerializer.ForwardReferencePlaceholder));
                    
                    il.Emit(OpCodes.Ldloc, valueLocal);
                    il.Emit(OpCodes.Isinst, typeof(NeoBinarySerializer.ForwardReferencePlaceholder));
                    il.Emit(OpCodes.Dup);
                    il.Emit(OpCodes.Stloc, placeholderLocal);
                    il.Emit(OpCodes.Brfalse_S, afterPlaceholderCheckLabel);

                    // Handle ForwardReferencePlaceholder - check for self-reference or track for later resolution
                    if (!type.IsValueType)
                    {
                        // Check if this is a self-reference by comparing placeholder ID with current object ID
                        var selfRefLabel = il.DefineLabel();
                        var trackRefLabel = il.DefineLabel();
                        var afterRefHandlingLabel = il.DefineLabel();
                        
                        // Get current object's ID using optimized reverse mapping
                        il.Emit(OpCodes.Ldloc, contextLocal);
                        il.Emit(OpCodes.Ldloc, typedObjLocal);
                        il.Emit(OpCodes.Call, typeof(IlTypeSerializer).GetMethod("FindObjectIdOptimized", BindingFlags.Public | BindingFlags.Static));
                        il.Emit(OpCodes.Ldloc, placeholderLocal);
                        il.Emit(OpCodes.Callvirt, typeof(NeoBinarySerializer.ForwardReferencePlaceholder).GetProperty("ObjectId").GetGetMethod());
                        il.Emit(OpCodes.Beq_S, selfRefLabel);
                        
                        // Not a self-reference - track for later resolution
                        il.Emit(OpCodes.Br_S, trackRefLabel);
                        
                        // Self-reference: set field to the object itself
                        il.MarkLabel(selfRefLabel);
                        il.Emit(OpCodes.Ldloc, typedObjLocal);
                        il.Emit(OpCodes.Ldloc, typedObjLocal);
                        il.Emit(OpCodes.Stfld, field);
                        il.Emit(OpCodes.Br_S, afterRefHandlingLabel);
                        
                        // Track forward reference for later resolution
                        il.MarkLabel(trackRefLabel);
                        il.Emit(OpCodes.Ldloc, contextLocal);
                        il.Emit(OpCodes.Ldloc, typedObjLocal);
                        il.Emit(OpCodes.Ldtoken, field);
                        il.Emit(OpCodes.Call, typeof(FieldInfo).GetMethod("GetFieldFromHandle", new[] { typeof(RuntimeFieldHandle) }));
                        il.Emit(OpCodes.Ldloc, placeholderLocal);
                        il.Emit(OpCodes.Callvirt, typeof(NeoBinarySerializer.ForwardReferencePlaceholder).GetProperty("ObjectId").GetGetMethod());
                        il.Emit(OpCodes.Call, typeof(IlTypeSerializer).GetMethod("TrackForwardReference", BindingFlags.NonPublic | BindingFlags.Static));
                        
                        // Initialize field to a safe default for now (will be resolved later)
                        if (field.FieldType.IsValueType)
                        {
                            // For value-type fields in a reference-type object: initobj on field address
                            il.Emit(OpCodes.Ldloc, typedObjLocal);
                            il.Emit(OpCodes.Ldflda, field);
                            il.Emit(OpCodes.Initobj, field.FieldType);
                        }
                        else
                        {
                            // For reference-type fields: set to null
                            il.Emit(OpCodes.Ldloc, typedObjLocal);
                            il.Emit(OpCodes.Ldnull);
                            il.Emit(OpCodes.Stfld, field);
                        }
                        
                        il.MarkLabel(afterRefHandlingLabel);
                        // Skip normal assignment for this field when placeholder encountered
                        il.Emit(OpCodes.Br_S, endOfFieldLabel);
                    }
                    else
                    {
                        // For value types, set the field to its default value.
                        // Need the address of the struct local, then the address of the field, then initobj on the field type.
                        il.Emit(OpCodes.Ldloca, typedObjLocal);
                        il.Emit(OpCodes.Ldflda, field);
                        il.Emit(OpCodes.Initobj, field.FieldType);
                        // Skip normal assignment for this field when placeholder encountered
                        il.Emit(OpCodes.Br_S, endOfFieldLabel);
                    }
                    
                    // Normal value handling
                    il.MarkLabel(afterPlaceholderCheckLabel);
                    
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

                    // End of field handling
                    il.MarkLabel(endOfFieldLabel);
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
        /// Creates a compact-layout deserializer delegate for specified type (no field names/count).
        /// </summary>
        public ObjectDeserializerDelegate CreateCompactDeserializer(Type type)
        {
            return _compactDeserializers.GetOrAdd(type, t =>
            {
                var fields = _fieldCache.GetOrAdd(type, GetAllFields);

                var dynamicMethod = new DynamicMethod(
                    $"DeserializeCompact_{type.Name}_{Guid.NewGuid():N}",
                    typeof(object),
                    new[] { typeof(BinaryReader), typeof(DeserializationContext) },
                    typeof(NeoBinarySerializer),
                    true);

                var il = dynamicMethod.GetILGenerator();
                var readerLocal = il.DeclareLocal(typeof(BinaryReader));
                var contextLocal = il.DeclareLocal(typeof(DeserializationContext));
                var typedObjLocal = il.DeclareLocal(type);
                var valueLocal = il.DeclareLocal(typeof(object));

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

                // For each field in fixed order: read value and assign
                foreach (var field in fields)
                {
                    var endOfFieldLabel = il.DefineLabel();

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

                    // Check placeholder
                    var afterPlaceholderCheckLabel = il.DefineLabel();
                    var placeholderLocal = il.DeclareLocal(typeof(NeoBinarySerializer.ForwardReferencePlaceholder));
                    var selfRefLabel = il.DefineLabel();
                    var trackRefLabel = il.DefineLabel();
                    var afterRefHandlingLabel = il.DefineLabel();

                    il.Emit(OpCodes.Ldloc, valueLocal);
                    il.Emit(OpCodes.Isinst, typeof(NeoBinarySerializer.ForwardReferencePlaceholder));
                    il.Emit(OpCodes.Dup);
                    il.Emit(OpCodes.Stloc, placeholderLocal);
                    il.Emit(OpCodes.Brfalse_S, afterPlaceholderCheckLabel);

                    if (!type.IsValueType)
                    {
                        // Compare current object id with placeholder id
                        il.Emit(OpCodes.Ldloc, contextLocal);
                        il.Emit(OpCodes.Ldloc, typedObjLocal);
                        il.Emit(OpCodes.Call, typeof(IlTypeSerializer).GetMethod("FindObjectIdOptimized", BindingFlags.Public | BindingFlags.Static));
                        il.Emit(OpCodes.Ldloc, placeholderLocal);
                        il.Emit(OpCodes.Callvirt, typeof(NeoBinarySerializer.ForwardReferencePlaceholder).GetProperty("ObjectId").GetGetMethod());
                        il.Emit(OpCodes.Beq_S, selfRefLabel);

                        il.Emit(OpCodes.Br_S, trackRefLabel);

                        il.MarkLabel(selfRefLabel);
                        il.Emit(OpCodes.Ldloc, typedObjLocal);
                        il.Emit(OpCodes.Ldloc, typedObjLocal);
                        il.Emit(OpCodes.Stfld, field);
                        il.Emit(OpCodes.Br_S, afterRefHandlingLabel);

                        il.MarkLabel(trackRefLabel);
                        il.Emit(OpCodes.Ldloc, contextLocal);
                        il.Emit(OpCodes.Ldloc, typedObjLocal);
                        il.Emit(OpCodes.Ldtoken, field);
                        il.Emit(OpCodes.Call, typeof(FieldInfo).GetMethod("GetFieldFromHandle", new[] { typeof(RuntimeFieldHandle) }));
                        il.Emit(OpCodes.Ldloc, placeholderLocal);
                        il.Emit(OpCodes.Callvirt, typeof(NeoBinarySerializer.ForwardReferencePlaceholder).GetProperty("ObjectId").GetGetMethod());
                        il.Emit(OpCodes.Call, typeof(IlTypeSerializer).GetMethod("TrackForwardReference", BindingFlags.NonPublic | BindingFlags.Static));

                        if (field.FieldType.IsValueType)
                        {
                            il.Emit(OpCodes.Ldloc, typedObjLocal);
                            il.Emit(OpCodes.Ldflda, field);
                            il.Emit(OpCodes.Initobj, field.FieldType);
                        }
                        else
                        {
                            il.Emit(OpCodes.Ldloc, typedObjLocal);
                            il.Emit(OpCodes.Ldnull);
                            il.Emit(OpCodes.Stfld, field);
                        }

                        il.MarkLabel(afterRefHandlingLabel);
                        il.Emit(OpCodes.Br_S, endOfFieldLabel);
                    }
                    else
                    {
                        // value type container: set field default
                        il.Emit(OpCodes.Ldloca, typedObjLocal);
                        il.Emit(OpCodes.Ldflda, field);
                        il.Emit(OpCodes.Initobj, field.FieldType);
                        il.Emit(OpCodes.Br_S, endOfFieldLabel);
                    }

                    il.MarkLabel(afterPlaceholderCheckLabel);

                    // Assign field
                    if (type.IsValueType)
                    {
                        il.Emit(OpCodes.Ldloca, typedObjLocal);
                    }
                    else
                    {
                        il.Emit(OpCodes.Ldloc, typedObjLocal);
                    }

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

                    il.MarkLabel(endOfFieldLabel);
                }

                // Return object
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
        /// Helper method to track forward references for later resolution.
        /// </summary>
        /// <param name="context">Deserialization context</param>
        /// <param name="targetObject">Object containing the field</param>
        /// <param name="field">Field info</param>
        /// <param name="placeholderObjectId">Placeholder object ID</param>
        private static void TrackForwardReference(DeserializationContext context, object targetObject, FieldInfo field, int placeholderObjectId)
        {
            context.ForwardReferences.Add((targetObject, field, placeholderObjectId));
        }

        /// <summary>
        /// Performance-optimized method to find object ID using reverse lookup.
        /// </summary>
        /// <param name="context">Deserialization context containing reverse mapping</param>
        /// <param name="targetObject">Target object to find ID for</param>
        /// <returns>Object ID or -1 if not found</returns>
        public static int FindObjectIdOptimized(DeserializationContext context, object targetObject)
        {
            if (context.ObjectToIdMap.TryGetValue(targetObject, out var objectId))
                return objectId;
            return -1;
        }

        /// <summary>
        /// Helper method to find object ID for a given object in deserialized objects dictionary.
        /// Optimized to use reverse lookup when available.
        /// </summary>
        /// <param name="deserializedObjects">Dictionary of deserialized objects</param>
        /// <param name="targetObject">Object to find ID for</param>
        /// <returns>Object ID if found, otherwise -1</returns>
        private static int FindObjectId(Dictionary<int, object> deserializedObjects, object targetObject)
        {
            // Performance optimization: Try to use reverse lookup first
            // This is a heuristic - in most cases, the deserializedObjects dictionary
            // will be part of a DeserializationContext with ObjectToIdMap populated
            if (deserializedObjects.Count > 0)
            {
                // Get the first entry to potentially access the context
                // This is a workaround to access the reverse mapping without changing IL generation
                var firstEntry = deserializedObjects.First();
                if (firstEntry.Value is NeoBinarySerializer.ForwardReferencePlaceholder)
                {
                    // Try to find the object through linear search as fallback
                    foreach (var kvp in deserializedObjects)
                    {
                        if (ReferenceEquals(kvp.Value, targetObject))
                        {
                            return kvp.Key;
                        }
                    }
                }
            }
            
            return -1;
        }

        /// <summary>
        /// Resolves all tracked forward references.
        /// </summary>
        /// <param name="context">Deserialization context</param>
        public static void ResolveForwardReferences(DeserializationContext context)
        {
            var maxIterations = context.ForwardReferences.Count + 1;
            
            for (int iteration = 0; iteration < maxIterations; iteration++)
            {
                var remainingReferences = new List<(object targetObject, FieldInfo field, int placeholderObjectId)>();
                
                foreach (var (targetObject, field, placeholderObjectId) in context.ForwardReferences)
                {
                    if (context.DeserializedObjects.TryGetValue(placeholderObjectId, out var resolvedObject)
                        && resolvedObject is not NeoBinarySerializer.ForwardReferencePlaceholder)
                    {
                        // Reference resolved - set the field value
                        field.SetValue(targetObject, resolvedObject);
                    }
                    else
                    {
                        remainingReferences.Add((targetObject, field, placeholderObjectId));
                    }
                }
                
                // Replace with only unresolved references
                context.ForwardReferences.Clear();
                foreach (var remaining in remainingReferences)
                {
                    context.ForwardReferences.Add(remaining);
                }
                
                if (context.ForwardReferences.Count == 0)
                    break;
            }
            
            if (context.ForwardReferences.Count > 0)
            {
                // Hand unresolved references to the owning serializer for final resolution
                foreach (var (targetObject, field, placeholderObjectId) in context.ForwardReferences)
                {
                    context.Serializer.AddPendingForwardReference(targetObject, field, placeholderObjectId);
                }
            }
        }

        /// <summary>
        /// Clears all cached serializers and deserializers.
        /// </summary>
        public void ClearCache()
        {
            _serializers.Clear();
            _deserializers.Clear();
            _compactSerializers.Clear();
            _compactDeserializers.Clear();
            _fieldCache.Clear();
        }
    }
}