namespace CoreRemoting.Serialization.Bson.Converters
{
    /// <summary>
    /// Enumeration for primitive types to avoid string serialization overhead.
    /// </summary>
    internal enum PrimitiveType : byte
    {
        Unknown = 0,
        Int32 = 1,
        Int64 = 2,
        String = 3,
        Boolean = 4,
        Double = 5,
        Float = 6,
        Decimal = 7,
        DateTime = 8,
        Char = 9,
        Byte = 10,
        SByte = 11,
        Int16 = 12,
        UInt16 = 13,
        UInt32 = 14,
        UInt64 = 15,
        Complex = 255
    }
}