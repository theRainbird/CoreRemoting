using System;
using System.Buffers.Text;
using System.Data;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CoreRemoting.Serialization.Bson.Converters.DataSetDiffGramSupport;

/// <summary>
/// Converter to serialize and deserialize typed and untyped DataSets / DataTables.
/// </summary>
public class DataSetDiffGramJsonConverter : JsonConverter
{
    /// <summary>
    /// Returns true if this converter can convert the specified object type.
    /// </summary>
    /// <param name="objectType">Object type</param>
    /// <returns>True if type can be converted, otherwise false</returns>
    public override bool CanConvert(Type objectType)
    {
        return
            typeof(DataTable).IsAssignableFrom(objectType) ||
            typeof(DataSet).IsAssignableFrom(objectType);
    }

    /// <summary>
    /// Writes JSON for a DataSet/DataTable instance.
    /// </summary>
    /// <param name="writer">JSON writer</param>
    /// <param name="value">DataSet/DataTable</param>
    /// <param name="serializer">JSON serializer</param>
    public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
    {
        if (value == null)
            throw new ArgumentNullException(nameof(value));

        var type = value.GetType();

        using var diffGramWriter = new StringWriter();
        using var schemaWriter = new StringWriter();
        
        if (typeof(DataTable).IsAssignableFrom(type))
        {
            var table = (DataTable)value;
            table.WriteXml(diffGramWriter, XmlWriteMode.DiffGram);
            table.WriteXmlSchema(schemaWriter);    
        }
        else if (typeof(DataSet).IsAssignableFrom(type))
        {
            var dataSet = (DataSet)value;
            dataSet.WriteXml(diffGramWriter, XmlWriteMode.DiffGram);
            dataSet.WriteXmlSchema(schemaWriter);
        }
        else
            throw new ArgumentException("Value is not a DataSet or DataTable.");

        var xmlSchema =
            Convert.ToBase64String(
                Encoding.UTF8.GetBytes(
                    schemaWriter.ToString()));

        var diffGram =
            Convert.ToBase64String(
                Encoding.UTF8.GetBytes(diffGramWriter.ToString()));

        var wrapper = new SerializedDiffGram()
        {
            XmlSchema = xmlSchema,
            DiffGram = diffGram
        };
        
        serializer.Serialize(writer, wrapper);
    }

    /// <summary>
    /// Reads a DataSet/DataTable instance from JSON.
    /// </summary>
    /// <param name="reader">JSON reader</param>
    /// <param name="objectType">Object type to be read</param>
    /// <param name="existingValue">Existing value</param>
    /// <param name="serializer">JSON serializer</param>
    /// <returns>DataSet/DataTable instance</returns>
    public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
    {
        var wrapper = serializer.Deserialize<SerializedDiffGram>(reader);

        return wrapper.Restore(objectType);
    }
}