using System;
using System.Data;
using System.IO;
using System.Text;

namespace CoreRemoting.Serialization.Bson.Converters.DataSetDiffGramSupport;

/// <summary>
/// Representation of serialized DataSet/DataTable DiffGram.
/// </summary>
public class SerializedDiffGram
{
    /// <summary>
    /// Gets or sets the serialized XML Schema.
    /// </summary>
    public string XmlSchema { get; set; }
    
    /// <summary>
    /// Gets or sets the serialized DiffGram data.
    /// </summary>
    public string DiffGram { get; set; }

    /// <summary>
    /// Restores the DataSet/DataTable.
    /// </summary>
    /// <param name="objectType">Type of the DataSet / DataTable</param>
    /// <returns>DataSet / DataTable instance created from serialized DiffGram</returns>
    public object Restore(Type objectType)
    {
        var xmlSchema = 
            Encoding.UTF8.GetString(
                Convert.FromBase64String(
                    XmlSchema));
        
        var diffGram = 
            Encoding.UTF8.GetString(
                Convert.FromBase64String(
                    DiffGram));
        
        using var schemaReader = new StringReader(xmlSchema);
        using var diffGramReader = new StringReader(diffGram);

        if (typeof(DataSet).IsAssignableFrom(objectType))
        {
            DataSet dataSet;
            
            if (objectType == typeof(DataSet))
                dataSet = new DataSet();
            else
                dataSet = (DataSet)Activator.CreateInstance(objectType);
            
            dataSet.ReadXmlSchema(schemaReader);
            dataSet.ReadXml(diffGramReader, XmlReadMode.DiffGram);

            return dataSet;
        }
        else if (typeof(DataTable).IsAssignableFrom(objectType))
        {
            DataTable table;

            if (objectType == typeof(DataTable))
                table = new DataTable();
            else
                table = (DataTable)Activator.CreateInstance(objectType);

            table.ReadXmlSchema(schemaReader);
            table.ReadXml(diffGramReader);

            return table;
        }
        else
            return null;
    }
}