using System.IO;

/*
 * Code is copied from https://github.com/zyanfx/SafeDeserializationHelpers
 * Many thanks to yallie for this great extensions to make BinaryFormatter a lot safer.
 */

namespace CoreRemoting.Serialization.Binary
{
    using System;
    using System.Runtime.Serialization.Formatters.Binary;

    /// <summary>
    /// Extension methods for the
    /// </summary>
    public static class BinaryFormatterExtensions
    {
        /// <summary>
        /// Makes the <see cref="BinaryFormatter"/> safe.
        /// </summary>
        /// <param name="formatter">The <see cref="BinaryFormatter"/> to guard.</param>
        /// <returns>The safe version of the <see cref="BinaryFormatter"/>.</returns>
        public static BinaryFormatter Safe(this BinaryFormatter formatter)
        {
            if (formatter == null)
            {
                throw new ArgumentNullException(nameof(formatter), "BinaryFormatter is not specified.");
            }
            
            // safe type binder prevents delegate deserialization attacks
            if (!(formatter.Binder is SafeSerializationBinder))
            {
                formatter.Binder = new SafeSerializationBinder(formatter.Binder);
            }

            // surrogates validate binary-serialized data before deserializing them
            if (!(formatter.SurrogateSelector is SafeSurrogateSelector))
            {
                // create a new surrogate selector and chain to the existing one, if any
                formatter.SurrogateSelector = new SafeSurrogateSelector(formatter.SurrogateSelector);
            }

            return formatter;
        }
        
        public static byte[] SerializeByteArray(this BinaryFormatter formatter, object objectToSerialize)
        {
            using var stream = new MemoryStream();
            formatter.Serialize(stream, objectToSerialize);
            return stream.ToArray();
        }
        
        public static object DeserializeSafe(this BinaryFormatter formatter, byte[] rawData)
        {
            var safeBinaryFormatter = formatter.Safe();
            using var stream = new MemoryStream(rawData);
            return safeBinaryFormatter.Deserialize(stream);
        }
    }
}