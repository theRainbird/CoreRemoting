using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;

namespace CoreRemoting.Serialization.NeoBinary;

/// <summary>
/// High-performance IL-based serializer for complex objects.
/// Generates optimized IL code at runtime for serialization and deserialization.
/// </summary>
public partial class IlTypeSerializer
{
    /// <summary>
    /// Subformat tag indicating the compact IL layout (no field names/count).
    /// Written immediately after type info for complex objects.
    /// </summary>
    public const byte COMPACT_LAYOUT_TAG = 0xFE;

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
    public delegate void ObjectSerializerDelegate(object obj, BinaryWriter writer, ObjectSerializationContext context);

    /// <summary>
    /// Delegate for object deserialization.
    /// </summary>
    /// <param name="reader">Binary reader</param>
    /// <param name="context">Deserialization context</param>
    /// <returns>Deserialized object</returns>
    public delegate object ObjectDeserializerDelegate(BinaryReader reader, ObjectDeserializationContext context);

    /// <summary>
    /// Gets all fields in the type hierarchy, including private fields from base types.
    /// </summary>
    /// <param name="type">Type to get fields for</param>
    /// <returns>Array of all fields</returns>
    private static FieldInfo[] GetAllFields(Type type)
    {
        // Skip IL generation for reflection types to avoid IL generation issues
        if (typeof(MemberInfo).IsAssignableFrom(type) ||
            typeof(ParameterInfo).IsAssignableFrom(type) ||
            typeof(Module).IsAssignableFrom(type) ||
            typeof(Assembly).IsAssignableFrom(type) ||
            typeof(AssemblyName).IsAssignableFrom(type))
            return []; // No field serialization for reflection types

        var fields = new List<FieldInfo>();
        var currentType = type;

        while (currentType != null && currentType != typeof(object))
        {
            // Match NeoBinarySerializer.GetAllFieldsInHierarchy ordering/selection
            var typeFields = currentType.GetFields(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            // Filter out infrastructure fields that must not be serialized
            foreach (var f in typeFields)
            {
                // Exclude static fields
                if (f.IsStatic) continue;

                // Exclude fields marked as [NonSerialized]
#pragma warning disable SYSLIB0050
                if (f.IsNotSerialized) continue;
#pragma warning restore SYSLIB0050

                var ft = f.FieldType;

                // Exclude delegate/event backing fields
                if (typeof(MulticastDelegate).IsAssignableFrom(ft) || typeof(Delegate).IsAssignableFrom(ft))
                    continue;

                // Exclude runtime, reflection, and threading infrastructure types
                var ns = ft.Namespace ?? string.Empty;
                if (ns.StartsWith("System.Reflection", StringComparison.Ordinal) ||
                    ns.StartsWith("System.Threading", StringComparison.Ordinal))
                    continue;

                // Exclude Type and runtime type handles (e.g., BusinessObjectBase._instanceType)
                if (typeof(Type).IsAssignableFrom(ft))
                    continue;

                // Exclude CoreRemoting CallContext infrastructure specifically (but allow other CoreRemoting DTOs)
                var fqn = ft.FullName ?? string.Empty;
                if (fqn.IndexOf("CoreRemoting.CallContextEntry", StringComparison.Ordinal) >= 0 ||
                    fqn.IndexOf("CoreRemoting.CallContext", StringComparison.Ordinal) >= 0)
                    continue;

                // Keep everything else
                fields.Add(f);
            }

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
        return _serializers.GetOrAdd(type, _ =>
        {
            var fields = _fieldCache.GetOrAdd(type, GetAllFields);

            // Bind the DynamicMethod to NeoBinarySerializer to allow access to its non-public instance methods
            var dynamicMethod = new DynamicMethod(
                $"Serialize_{type.Name}_{Guid.NewGuid():N}",
                typeof(void),
                [typeof(object), typeof(BinaryWriter), typeof(ObjectSerializationContext)],
                typeof(NeoBinarySerializer),
                true);

            var il = dynamicMethod.GetILGenerator();

            var writerLocal = il.DeclareLocal(typeof(BinaryWriter));
            var contextLocal = il.DeclareLocal(typeof(ObjectSerializationContext));
            var typedObjLocal = il.DeclareLocal(type);

            // Store args to locals
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Stloc, writerLocal);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Stloc, contextLocal);
            il.Emit(OpCodes.Ldarg_0);
            // Unbox the boxed struct to the value-type local
            il.Emit(type.IsValueType ? OpCodes.Unbox_Any : OpCodes.Castclass, type);
            il.Emit(OpCodes.Stloc, typedObjLocal);

            // Write field count to match general serializer format
            il.Emit(OpCodes.Ldloc, writerLocal);
            il.Emit(OpCodes.Ldc_I4, fields.Length);
            il.Emit(OpCodes.Callvirt, typeof(BinaryWriter).GetMethod("Write", [typeof(int)])!);

            // For each field: write name and serialize value via NeoBinarySerializer.SerializeObject
            foreach (var field in fields)
            {
                // writer.Write(fieldName)
                il.Emit(OpCodes.Ldloc, writerLocal);
                il.Emit(OpCodes.Ldstr, field.Name);
                il.Emit(OpCodes.Callvirt, typeof(BinaryWriter).GetMethod("Write", [typeof(string)])!);

                // Load serializer instance from context
                il.Emit(OpCodes.Ldloc, contextLocal);
                il.Emit(OpCodes.Callvirt, typeof(ObjectSerializationContext).GetProperty("Serializer")!.GetGetMethod());

                // arg0: object value
                // For value-type container, load address before Ldfld
                il.Emit(type.IsValueType ? OpCodes.Ldloca : OpCodes.Ldloc, typedObjLocal);
                il.Emit(OpCodes.Ldfld, field);
                if (field.FieldType.IsValueType) il.Emit(OpCodes.Box, field.FieldType);

                // arg1: BinaryWriter
                il.Emit(OpCodes.Ldloc, writerLocal);

                // arg2: HashSet<object> serializedObjects
                il.Emit(OpCodes.Ldloc, contextLocal);
                il.Emit(OpCodes.Callvirt,
                    typeof(ObjectSerializationContext).GetProperty("SerializedObjects")!.GetGetMethod());

                // arg3: Dictionary<object,int> objectMap
                il.Emit(OpCodes.Ldloc, contextLocal);
                il.Emit(OpCodes.Callvirt, typeof(ObjectSerializationContext).GetProperty("ObjectMap")!.GetGetMethod());

                // call NeoBinarySerializer.SerializeObject(object, BinaryWriter, HashSet<object>, Dictionary<object,int>)
                il.Emit(OpCodes.Callvirt, typeof(NeoBinarySerializer).GetMethod(
                    "SerializeObject",
                    BindingFlags.NonPublic | BindingFlags.Instance)!);
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
        return _compactSerializers.GetOrAdd(type, _ =>
        {
            var fields = _fieldCache.GetOrAdd(type, GetAllFields);

            var dynamicMethod = new DynamicMethod(
                $"SerializeCompact_{type.Name}_{Guid.NewGuid():N}",
                typeof(void),
                [typeof(object), typeof(BinaryWriter), typeof(ObjectSerializationContext)],
                typeof(NeoBinarySerializer),
                true);

            var il = dynamicMethod.GetILGenerator();

            var writerLocal = il.DeclareLocal(typeof(BinaryWriter));
            var contextLocal = il.DeclareLocal(typeof(ObjectSerializationContext));
            var typedObjLocal = il.DeclareLocal(type);

            // Store args to locals
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Stloc, writerLocal);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Stloc, contextLocal);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(type.IsValueType ? OpCodes.Unbox_Any : OpCodes.Castclass, type);
            il.Emit(OpCodes.Stloc, typedObjLocal);

            // For each field: directly serialize value (no field names/count)
            foreach (var field in fields)
            {
                // Load serializer instance from context
                il.Emit(OpCodes.Ldloc, contextLocal);
                il.Emit(OpCodes.Callvirt, typeof(ObjectSerializationContext).GetProperty("Serializer")!.GetGetMethod());

                // arg0: object value
                il.Emit(type.IsValueType ? OpCodes.Ldloca : OpCodes.Ldloc, typedObjLocal);
                il.Emit(OpCodes.Ldfld, field);
                if (field.FieldType.IsValueType) il.Emit(OpCodes.Box, field.FieldType);

                // arg1: BinaryWriter
                il.Emit(OpCodes.Ldloc, writerLocal);

                // arg2: HashSet<object> serializedObjects
                il.Emit(OpCodes.Ldloc, contextLocal);
                il.Emit(OpCodes.Callvirt,
                    typeof(ObjectSerializationContext).GetProperty("SerializedObjects")!.GetGetMethod());

                // arg3: Dictionary<object,int> objectMap
                il.Emit(OpCodes.Ldloc, contextLocal);
                il.Emit(OpCodes.Callvirt, typeof(ObjectSerializationContext).GetProperty("ObjectMap")!.GetGetMethod());

                // call NeoBinarySerializer.SerializeObject(object, BinaryWriter, HashSet<object>, Dictionary<object,int>)
                il.Emit(OpCodes.Callvirt, typeof(NeoBinarySerializer).GetMethod(
                    "SerializeObject",
                    BindingFlags.NonPublic | BindingFlags.Instance)!);
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
        return _deserializers.GetOrAdd(type, _ =>
        {
            var fields = _fieldCache.GetOrAdd(type, GetAllFields);

            // Bind the DynamicMethod to NeoBinarySerializer to allow access to its non-public instance methods
            var dynamicMethod = new DynamicMethod(
                $"Deserialize_{type.Name}_{Guid.NewGuid():N}",
                typeof(object),
                [typeof(BinaryReader), typeof(ObjectDeserializationContext)],
                typeof(NeoBinarySerializer),
                true);

            var il = dynamicMethod.GetILGenerator();
            var readerLocal = il.DeclareLocal(typeof(BinaryReader));
            var contextLocal = il.DeclareLocal(typeof(ObjectDeserializationContext));
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
                il.Emit(OpCodes.Callvirt,
                    typeof(ObjectDeserializationContext).GetProperty("Serializer")!.GetGetMethod());
                il.Emit(OpCodes.Ldtoken, type);
                il.Emit(OpCodes.Call, typeof(Type).GetMethod("GetTypeFromHandle")!);
                il.Emit(OpCodes.Callvirt, typeof(NeoBinarySerializer).GetMethod(
                    "CreateInstanceWithoutConstructor",
                    BindingFlags.NonPublic | BindingFlags.Instance)!);
                il.Emit(OpCodes.Castclass, type);
                il.Emit(OpCodes.Stloc, typedObjLocal);
            }

            // Read and ignore field count (kept for format parity and forward-compat)
            il.Emit(OpCodes.Ldloc, readerLocal);
            il.Emit(OpCodes.Call, typeof(BinaryReader).GetMethod("ReadInt32")!);
            il.Emit(OpCodes.Stloc, countLocal);

            // For each field, read name, then value, then assign
            foreach (var field in fields)
            {
                // Label to jump to end of this field handling when special-case path is taken
                var endOfFieldLabel = il.DefineLabel();

                // Read and discard field name
                il.Emit(OpCodes.Ldloc, readerLocal);
                il.Emit(OpCodes.Call, typeof(BinaryReader).GetMethod("ReadString")!);
                il.Emit(OpCodes.Pop);

                // value = context.Serializer.DeserializeObject(reader, deserializedObjects)
                il.Emit(OpCodes.Ldloc, contextLocal);
                il.Emit(OpCodes.Callvirt,
                    typeof(ObjectDeserializationContext).GetProperty("Serializer")!.GetGetMethod());
                il.Emit(OpCodes.Ldloc, readerLocal);
                il.Emit(OpCodes.Ldloc, contextLocal);
                il.Emit(OpCodes.Callvirt,
                    typeof(ObjectDeserializationContext).GetProperty("DeserializedObjects")!.GetGetMethod());
                il.Emit(OpCodes.Callvirt, typeof(NeoBinarySerializer).GetMethod(
                    "DeserializeObject",
                    BindingFlags.NonPublic | BindingFlags.Instance)!);
                il.Emit(OpCodes.Stloc, valueLocal);

                // Check if value is ForwardReferencePlaceholder
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
                    il.Emit(OpCodes.Call,
                        typeof(IlTypeSerializer).GetMethod("FindObjectIdOptimized",
                            BindingFlags.Public | BindingFlags.Static)!);
                    il.Emit(OpCodes.Ldloc, placeholderLocal);
                    il.Emit(OpCodes.Callvirt,
                        typeof(NeoBinarySerializer.ForwardReferencePlaceholder).GetProperty("ObjectId")!.GetGetMethod());
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
                    il.Emit(OpCodes.Call,
                        typeof(FieldInfo).GetMethod("GetFieldFromHandle", [typeof(RuntimeFieldHandle)])!);
                    il.Emit(OpCodes.Ldloc, placeholderLocal);
                    il.Emit(OpCodes.Callvirt,
                        typeof(NeoBinarySerializer.ForwardReferencePlaceholder).GetProperty("ObjectId")!.GetGetMethod());
                    il.Emit(OpCodes.Call,
                        typeof(IlTypeSerializer).GetMethod("TrackForwardReference",
                            BindingFlags.NonPublic | BindingFlags.Static)!);

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
                // Load address of value-type local
                il.Emit(type.IsValueType ? OpCodes.Ldloca : OpCodes.Ldloc, typedObjLocal);

                // Load value and cast/unbox to field type
                il.Emit(OpCodes.Ldloc, valueLocal);
                il.Emit(field.FieldType.IsValueType ? OpCodes.Unbox_Any : OpCodes.Castclass, field.FieldType);

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
        return _compactDeserializers.GetOrAdd(type, _ =>
        {
            var fields = _fieldCache.GetOrAdd(type, GetAllFields);

            var dynamicMethod = new DynamicMethod(
                $"DeserializeCompact_{type.Name}_{Guid.NewGuid():N}",
                typeof(object),
                [typeof(BinaryReader), typeof(ObjectDeserializationContext)],
                typeof(NeoBinarySerializer),
                true);

            var il = dynamicMethod.GetILGenerator();
            var readerLocal = il.DeclareLocal(typeof(BinaryReader));
            var contextLocal = il.DeclareLocal(typeof(ObjectDeserializationContext));
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
                il.Emit(OpCodes.Callvirt,
                    typeof(ObjectDeserializationContext).GetProperty("Serializer")!.GetGetMethod());
                il.Emit(OpCodes.Ldtoken, type);
                il.Emit(OpCodes.Call, typeof(Type).GetMethod("GetTypeFromHandle")!);
                il.Emit(OpCodes.Callvirt,
                    typeof(NeoBinarySerializer).GetMethod("CreateInstanceWithoutConstructor",
                        BindingFlags.NonPublic | BindingFlags.Instance)!);
                il.Emit(OpCodes.Castclass, type);
                il.Emit(OpCodes.Stloc, typedObjLocal);
            }

            // For each field in fixed order: read value and assign
            foreach (var field in fields)
            {
                var endOfFieldLabel = il.DefineLabel();

                // value = context.Serializer.DeserializeObject(reader, deserializedObjects)
                il.Emit(OpCodes.Ldloc, contextLocal);
                il.Emit(OpCodes.Callvirt,
                    typeof(ObjectDeserializationContext).GetProperty("Serializer")!.GetGetMethod());
                il.Emit(OpCodes.Ldloc, readerLocal);
                il.Emit(OpCodes.Ldloc, contextLocal);
                il.Emit(OpCodes.Callvirt,
                    typeof(ObjectDeserializationContext).GetProperty("DeserializedObjects")!.GetGetMethod());
                il.Emit(OpCodes.Callvirt,
                    typeof(NeoBinarySerializer).GetMethod("DeserializeObject",
                        BindingFlags.NonPublic | BindingFlags.Instance)!);
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
                    il.Emit(OpCodes.Call,
                        typeof(IlTypeSerializer).GetMethod("FindObjectIdOptimized",
                            BindingFlags.Public | BindingFlags.Static)!);
                    il.Emit(OpCodes.Ldloc, placeholderLocal);
                    il.Emit(OpCodes.Callvirt,
                        typeof(NeoBinarySerializer.ForwardReferencePlaceholder).GetProperty("ObjectId")!
                            .GetGetMethod());
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
                    il.Emit(OpCodes.Call,
                        typeof(FieldInfo).GetMethod("GetFieldFromHandle", [typeof(RuntimeFieldHandle)])!);
                    il.Emit(OpCodes.Ldloc, placeholderLocal);
                    il.Emit(OpCodes.Callvirt,
                        typeof(NeoBinarySerializer.ForwardReferencePlaceholder).GetProperty("ObjectId")!.GetGetMethod());
                    il.Emit(OpCodes.Call,
                        typeof(IlTypeSerializer).GetMethod("TrackForwardReference",
                            BindingFlags.NonPublic | BindingFlags.Static)!);

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
                il.Emit(type.IsValueType ? OpCodes.Ldloca : OpCodes.Ldloc, typedObjLocal);

                il.Emit(OpCodes.Ldloc, valueLocal);
                il.Emit(field.FieldType.IsValueType ? OpCodes.Unbox_Any : OpCodes.Castclass, field.FieldType);
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
    [SuppressMessage("ReSharper", "UnusedMember.Local")]
    private static void TrackForwardReference(ObjectDeserializationContext context, object targetObject, FieldInfo field,
        int placeholderObjectId)
    {
        context.ForwardReferences.Add((targetObject, field, placeholderObjectId));
    }    
    
    /// <summary>
    /// Performance-optimized method to find object ID using reverse lookup.
    /// </summary>
    /// <param name="context">Deserialization context containing reverse mapping</param>
    /// <param name="targetObject">Target object to find ID for</param>
    /// <returns>Object ID or -1 if not found</returns>
    public static int FindObjectIdOptimized(ObjectDeserializationContext context, object targetObject)
    {
        if (context.ObjectToIdMap.TryGetValue(targetObject, out var objectId))
            return objectId;
        return -1;
    }

    /// <summary>
    /// Resolves all tracked forward references.
    /// </summary>
    /// <param name="context">Deserialization context</param>
    public static void ResolveForwardReferences(ObjectDeserializationContext context)
    {
        var maxIterations = context.ForwardReferences.Count + 1;

        for (var iteration = 0; iteration < maxIterations; iteration++)
        {
            var remainingReferences = new List<(object targetObject, FieldInfo field, int placeholderObjectId)>();

            foreach (var (targetObject, field, placeholderObjectId) in context.ForwardReferences)
                if (context.DeserializedObjects.TryGetValue(placeholderObjectId, out var resolvedObject)
                    && resolvedObject is not NeoBinarySerializer.ForwardReferencePlaceholder)
                    // Reference resolved - set the field value
                    field.SetValue(targetObject, resolvedObject);
                else
                    remainingReferences.Add((targetObject, field, placeholderObjectId));

            // Replace with only unresolved references
            context.ForwardReferences.Clear();
            foreach (var remaining in remainingReferences) context.ForwardReferences.Add(remaining);

            if (context.ForwardReferences.Count == 0)
                break;
        }

        if (context.ForwardReferences.Count <= 0)
            return;

        // Hand unresolved references to the owning serializer for final resolution
        foreach (var (targetObject, field, placeholderObjectId) in context.ForwardReferences)
            context.Serializer.AddPendingForwardReference(targetObject, field, placeholderObjectId);
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