/*
 * Code is copied from https://github.com/zyanfx/SafeDeserializationHelpers
 * Many thanks to yallie for this great extensions to make BinaryFormatter a lot safer.
 */

using System.Diagnostics.CodeAnalysis;

namespace CoreRemoting.Serialization.Binary
{
    using System.Data;
    using System.IO;
    using System.Reflection;
    using System.Runtime.Serialization;
    using System.Runtime.Serialization.Formatters.Binary;
    using System.Security.Permissions;

    /// <summary>
    /// Deserialization surrogate for the DataSet class.
    /// </summary>
    internal class DataSetSurrogate : ISerializationSurrogate
    {
        private static ConstructorInfo Constructor { get; } = typeof(DataSet).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            null,
            new[] { typeof(SerializationInfo), typeof(StreamingContext) },
            null);

        /// <inheritdoc cref="ISerializationSurrogate" />
        [SecurityPermission(SecurityAction.LinkDemand, Flags = SecurityPermissionFlag.SerializationFormatter)]
        [SuppressMessage("ReSharper", "PossibleNullReferenceException")]
        public void GetObjectData(object obj, SerializationInfo info, StreamingContext context)
        {
            var ds = obj as DataSet;
            ds.GetObjectData(info, context);
        }

        /// <inheritdoc cref="ISerializationSurrogate" />
        [SecurityPermission(SecurityAction.LinkDemand, Flags = SecurityPermissionFlag.SerializationFormatter)]
        public object SetObjectData(object obj, SerializationInfo info, StreamingContext context, ISurrogateSelector selector)
        {
            Validate(info, context);

            // discard obj
            var ds = Constructor.Invoke(new object[] { info, context });
            return ds;
        }

        [SuppressMessage("ReSharper", "AssignNullToNotNullAttribute")]
        private void Validate(SerializationInfo info, StreamingContext context)
        {
            var remotingFormat = SerializationFormat.Xml;

            var e = info.GetEnumerator();
            while (e.MoveNext())
            {
                if (e.Name == "DataSet.RemotingFormat") // DataSet.RemotingFormat does not exist in V1/V1.1 versions
                    remotingFormat = (SerializationFormat) e.Value;
            }

            // XML dataset serialization isn't known to be vulnerable
            if (remotingFormat == SerializationFormat.Xml)
            {
                return;
            }

            // binary dataset serialization should be double-checked
            var tableCount = info.GetInt32("DataSet.Tables.Count");
            for (int i = 0; i < tableCount; i++)
            {
                var key = $"DataSet.Tables_{i}";
                var buffer = info.GetValue(key, typeof(byte[])) as byte[];

                // check the serialized data table using a guarded BinaryFormatter
                var fmt = 
                    new BinaryFormatter(
                        selector: null,
                        context: new StreamingContext(context.State, false))
                    .Safe();
                
                using var ms = new MemoryStream(buffer);
                
                var dt = fmt.Deserialize(ms);
                
                if (dt is DataTable)
                {
                    continue;
                }

                // the deserialized data doesn't appear to be a data table
                throw new UnsafeDeserializationException("Serialized DataSet probably includes malicious data.");
            }
        }
    }
}