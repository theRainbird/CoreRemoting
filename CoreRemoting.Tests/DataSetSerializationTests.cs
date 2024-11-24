using System.Data;
using System.IO;
using System.Reflection;
using CoreRemoting.Serialization.Bson.DataSetDiffGramSupport;
using Newtonsoft.Json;
using Xunit;

namespace CoreRemoting.Tests;

public class DataSetSerializationTests
{
    [Fact]
    public void DataSetDiffGramJsonConverter_should_serialize_typed_DataSet_to_DiffGram()
    {
        var originalDataSet = new TestDataSet();
        var originalRow = originalDataSet.TestTable.NewTestTableRow();
        originalRow.UserName = "TestUser";
        originalRow.Age = 44;
        originalDataSet.TestTable.AddTestTableRow(originalRow);
        originalDataSet.AcceptChanges();

        originalRow.Age = 43;
        
        var json =
            JsonConvert.SerializeObject(
                value: originalDataSet,
                formatting: Formatting.Indented,
                converters: new DataSetDiffGramJsonConverter());

        var deserializedDataSet =
            JsonConvert.DeserializeObject<TestDataSet>(
                value: json,
                converters: new DataSetDiffGramJsonConverter());
        
        var deserializedRow = deserializedDataSet.TestTable[0];
        
        Assert.Equal(originalDataSet.DataSetName, deserializedDataSet.DataSetName);
        Assert.Equal(originalDataSet.Tables.Count, deserializedDataSet.Tables.Count);
        Assert.Equal(originalRow.RowState, deserializedRow.RowState);
        Assert.Equal(originalRow["Age", DataRowVersion.Original], deserializedRow["Age", DataRowVersion.Original]);
        Assert.Equal(originalRow["Age", DataRowVersion.Current], deserializedRow["Age", DataRowVersion.Current]);
        Assert.Equal(originalRow["UserName", DataRowVersion.Current], deserializedRow["UserName", DataRowVersion.Current]);
    }
    
    [Fact]
    public void DataSetDiffGramJsonConverter_should_serialize_typed_DataTable_to_DiffGram()
    {
        var originalTable = new TestDataSet.TestTableDataTable();
        var originalRow = originalTable.NewTestTableRow();
        originalRow.UserName = "TestUser";
        originalRow.Age = 44;
        originalTable.AddTestTableRow(originalRow);
        originalTable.AcceptChanges();

        originalRow.Age = 43;
        
        var json =
            JsonConvert.SerializeObject(
                value: originalTable,
                formatting: Formatting.Indented,
                converters: new DataSetDiffGramJsonConverter());

        var deserializedTable =
            JsonConvert.DeserializeObject<TestDataSet.TestTableDataTable>(
                value: json,
                converters: new DataSetDiffGramJsonConverter());
        
        var deserializedRow = deserializedTable[0];
        
        Assert.Equal(originalRow.RowState, deserializedRow.RowState);
        Assert.Equal(originalRow["Age", DataRowVersion.Original], deserializedRow["Age", DataRowVersion.Original]);
        Assert.Equal(originalRow["Age", DataRowVersion.Current], deserializedRow["Age", DataRowVersion.Current]);
        Assert.Equal(originalRow["UserName", DataRowVersion.Current], deserializedRow["UserName", DataRowVersion.Current]);
    }
    
    [Fact]
    public void DataSetDiffGramJsonConverter_should_serialize_untyped_DataSet_to_DiffGram()
    {
        var originalTable = new DataTable("TestTable");
        originalTable.Columns.Add("UserName", typeof(string));
        originalTable.Columns.Add("Age", typeof(short));
        var originalDataSet = new DataSet("TestDataSet");
        originalDataSet.Tables.Add(originalTable);

        var originalRow = originalTable.NewRow();
        originalRow["UserName"] = "Tester";
        originalRow["Age"] = 44;
        originalTable.Rows.Add(originalRow);
        
        originalTable.AcceptChanges();

        originalRow["Age"] = 43;

        var json =
            JsonConvert.SerializeObject(
                value: originalDataSet,
                formatting: Formatting.Indented,
                converters: new DataSetDiffGramJsonConverter());

        var deserializedDataSet =
            JsonConvert.DeserializeObject<DataSet>(
                value: json,
                converters: new DataSetDiffGramJsonConverter());

        var deserializedTable = deserializedDataSet.Tables["TestTable"];
        var deserializedRow = deserializedTable!.Rows[0];
        
        Assert.Equal(originalDataSet.DataSetName, deserializedDataSet.DataSetName);
        Assert.Equal(originalTable.TableName, deserializedTable.TableName);
        Assert.Equal(originalRow.RowState, deserializedRow.RowState);
        Assert.Equal(originalRow["Age", DataRowVersion.Original], deserializedRow["Age", DataRowVersion.Original]);
        Assert.Equal(originalRow["Age", DataRowVersion.Current], deserializedRow["Age", DataRowVersion.Current]);
        Assert.Equal(originalRow["UserName", DataRowVersion.Current], deserializedRow["UserName", DataRowVersion.Current]);
    }
    
    [Fact]
    public void DataSetDiffGramJsonConverter_should_serialize_untyped_DataTable_to_DiffGram()
    {
        var originalTable = new DataTable("TestTable");
        originalTable.Columns.Add("UserName", typeof(string));
        originalTable.Columns.Add("Age", typeof(short));
        
        var originalRow = originalTable.NewRow();
        originalRow["UserName"] = "Tester";
        originalRow["Age"] = 44;
        originalTable.Rows.Add(originalRow);
        
        originalTable.AcceptChanges();

        originalRow["Age"] = 43;

        var json =
            JsonConvert.SerializeObject(
                value: originalTable,
                formatting: Formatting.Indented,
                converters: new DataSetDiffGramJsonConverter());

        var deserializedDataSet =
            JsonConvert.DeserializeObject<DataSet>(
                value: json,
                converters: new DataSetDiffGramJsonConverter());

        var deserializedTable = deserializedDataSet.Tables["TestTable"];
        var deserializedRow = deserializedTable!.Rows[0];
        
        Assert.Equal(originalTable.TableName, deserializedTable.TableName);
        Assert.Equal(originalRow.RowState, deserializedRow.RowState);
        Assert.Equal(originalRow["Age", DataRowVersion.Original], deserializedRow["Age", DataRowVersion.Original]);
        Assert.Equal(originalRow["Age", DataRowVersion.Current], deserializedRow["Age", DataRowVersion.Current]);
        Assert.Equal(originalRow["UserName", DataRowVersion.Current], deserializedRow["UserName", DataRowVersion.Current]);
    }
}