using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Numerics;
using System.Text;
using CoreRemoting.RpcMessaging;
using CoreRemoting.Serialization.Bson;
using CoreRemoting.Tests.Tools;
using Newtonsoft.Json;
using Xunit;

namespace CoreRemoting.Tests;

public class BsonSerializationTests
{
    #region Fake DateTime Json Converter

    private class FakeDateTimeConverter : JsonConverter
    {
        public int WriteCount { get; private set; }

        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(DateTime) || objectType == typeof(string);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            WriteCount++;
            writer.WriteValue(value);
        }
    }

    #endregion

    [Fact]
    public void BsonSerializerAdapter_should_deserialize_MethodCallMessage()
    {
        var serializer = new BsonSerializerAdapter();
        var testServiceInterfaceType = typeof(ITestService);
        
        var messageBuilder = new MethodCallMessageBuilder();

        var message =
            messageBuilder.BuildMethodCallMessage(
                serializer,
                testServiceInterfaceType.Name,
                testServiceInterfaceType.GetMethod("TestMethod"),
                [4711]);

        var rawData = serializer.Serialize(message);
        
        var deserializedMessage = serializer.Deserialize<MethodCallMessage>(rawData);
        
        deserializedMessage.UnwrapParametersFromDeserializedMethodCallMessage(
            out var parameterValues,
            out var parameterTypes);

        var parametersLength = deserializedMessage.Parameters.Length;
        
        Assert.Equal(1, parametersLength);
        Assert.NotNull(deserializedMessage.Parameters[0]);
        Assert.Equal("arg", deserializedMessage.Parameters[0].ParameterName);
        Assert.StartsWith("System.Object,", deserializedMessage.Parameters[0].ParameterTypeName);
        Assert.Equal(typeof(int), parameterValues[0].GetType());
        Assert.Equal(typeof(object), parameterTypes[0]);
        Assert.Equal(4711, parameterValues[0]);
    }

    [Fact]
    public void BsonSerializerAdapter_should_deserialize_CompleteHandshakeWireMessage()
    {
        var sessionId = Guid.NewGuid();
        
        var completeHandshakeMessage =
            new WireMessage
            {
                MessageType = "complete_handshake",
                Data = sessionId.ToByteArray()
            };   
        
        var serializer = new BsonSerializerAdapter();
        var rawData = serializer.Serialize(completeHandshakeMessage);

        var deserializedMessage = serializer.Deserialize<WireMessage>(rawData);
        
        Assert.Equal("complete_handshake", deserializedMessage.MessageType);
        Assert.Equal(sessionId, new Guid(deserializedMessage.Data));
    }

    [Fact]
    public void BsonSerializerAdapter_should_use_configured_JsonConverters()
    {
        var fakeConverter = new FakeDateTimeConverter();
        var config = new BsonSerializerConfig(
        [
            fakeConverter
        ]);

        var serializerAdapter = new BsonSerializerAdapter(config);

        var dateToSerialize = DateTime.Today;
        var raw = serializerAdapter.Serialize(dateToSerialize);
        
        Assert.NotEqual(0, fakeConverter.WriteCount);

        var deserializedDate = serializerAdapter.Deserialize<DateTime>(raw);

        Assert.Equal(dateToSerialize, deserializedDate);
    }

    private class PrimitiveValuesContainer
    {
        public byte ByteValue { get; set; }
        public sbyte SByteValue { get; set; }
        public short Int16Value { get; set; }
        public ushort UInt16Value { get; set; }
        public int Int32Value { get; set; }
        public uint UInt32Value { get; set; }
        public long Int64Value { get; set; }
        public ulong UInt64Value { get; set; }
        public float SingleValue { get; set; }
        public double DoubleValue { get; set; }
        public decimal DecimalValue { get; set; }
        public bool BoolValue { get; set; }
        public DateTime DateTimeValue { get; set; }
        public Guid GuidValue { get; set; }
    }

    [Fact]
    public void BsonSerializerAdapter_should_deserialize_primitive_properties_correctly()
    {
        var csharpDateTime = DateTime.Now;
        var ticksTruncatedCSharpDate =
            new DateTime(
                csharpDateTime.Year,
                csharpDateTime.Month,
                csharpDateTime.Day,
                csharpDateTime.Hour,
                csharpDateTime.Minute,
                csharpDateTime.Second,
                csharpDateTime.Millisecond);
        
        var test = new PrimitiveValuesContainer()
        {
            ByteValue = byte.MaxValue,
            SByteValue = sbyte.MinValue,
            BoolValue = true,
            DecimalValue = 10^6145,
            SingleValue = float.MaxValue,
            DoubleValue = double.MaxValue,
            GuidValue = Guid.NewGuid(),
            Int16Value = short.MaxValue,
            Int32Value = int.MaxValue,
            Int64Value = long.MaxValue,
            DateTimeValue = ticksTruncatedCSharpDate,
            UInt16Value = ushort.MaxValue,
            UInt32Value = int.MaxValue, // BSON doesn't support integer values larger than Int32
            UInt64Value = int.MaxValue // BSON doesn't support integer values larger than Int32
        };

        var serializer = new BsonSerializerAdapter();
        var raw = serializer.Serialize(test);
        var deserializedTest = serializer.Deserialize<PrimitiveValuesContainer>(raw);
        
        Assert.Equal(test.ByteValue, deserializedTest.ByteValue);
        Assert.Equal(test.SByteValue, deserializedTest.SByteValue);
        Assert.Equal(test.BoolValue, deserializedTest.BoolValue);
        Assert.Equal(test.DecimalValue, deserializedTest.DecimalValue);
        Assert.Equal(test.DoubleValue, deserializedTest.DoubleValue);
        Assert.Equal(test.SingleValue, deserializedTest.SingleValue);
        Assert.Equal(test.GuidValue, deserializedTest.GuidValue);
        Assert.Equal(test.Int16Value, deserializedTest.Int16Value);
        Assert.Equal(test.Int32Value, deserializedTest.Int32Value);
        Assert.Equal(test.Int64Value, deserializedTest.Int64Value);
        Assert.Equal(test.DateTimeValue, deserializedTest.DateTimeValue);
        Assert.Equal(test.UInt16Value, deserializedTest.UInt16Value);
        Assert.Equal(test.UInt32Value, deserializedTest.UInt32Value);
        Assert.Equal(test.UInt64Value, deserializedTest.UInt64Value);
    }

    [Fact]
    public void BsonSerializerAdapter_should_deserialize_Int32_value_in_envelope_correctly()
    {
        var envelope = new Envelope(Int32.MaxValue);
        
        var serializer = new BsonSerializerAdapter();
        var raw = serializer.Serialize(envelope);
        var deserializedValue = serializer.Deserialize<Envelope>(raw);

        Assert.Equal(envelope.Value, deserializedValue.Value);
        Assert.IsType<Int32>(envelope.Value);
    }

    [Fact]
    public void BsonSerializerAdapter_should_serialize_DataSet_as_Diffgram()
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

        var envelope = new Envelope(originalDataSet);
        
        var serializer = new BsonSerializerAdapter();
        var raw = serializer.Serialize(envelope);
        var deserializedEnvelope = serializer.Deserialize<Envelope>(raw);

        var deserializedDataSet = (DataSet)deserializedEnvelope.Value;
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
    public void BsonSerializerAdapter_should_deserialize_common_dotnet_types_correctly()
    {
        var serializer = new BsonSerializerAdapter();

        void SerializeAndDeserialize<T>(T valueToSerialize)
        {
            var raw = serializer.Serialize(valueToSerialize);
            var deserializedValue = serializer.Deserialize<T>(raw);

            Assert.Equal(valueToSerialize, deserializedValue);
        }

        SerializeAndDeserialize(new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.FromMinutes(42)));
        SerializeAndDeserialize(new DateTime(2025, 10, 1, 6, 11, 52, 123, DateTimeKind.Unspecified));
        SerializeAndDeserialize(new DateTime(2025, 10, 1, 6, 11, 52, 321, DateTimeKind.Local));
        SerializeAndDeserialize(new DateTime(2025, 10, 1, 6, 11, 52, 321, DateTimeKind.Utc));
        SerializeAndDeserialize(TimeSpan.FromSeconds(42));
        SerializeAndDeserialize(new Uri("http://127.0.0.1"));
        SerializeAndDeserialize(new RegionInfo("US"));
        SerializeAndDeserialize(new Version(1, 0));
        SerializeAndDeserialize(new BigInteger(42));
        SerializeAndDeserialize(IPAddress.Parse("127.0.0.1"));
        SerializeAndDeserialize(new IPEndPoint(IPAddress.Parse("127.0.0.1"), 80));
        SerializeAndDeserialize(typeof(Type));

        foreach (var cultureInfo in CultureInfo.GetCultures(CultureTypes.AllCultures))
            SerializeAndDeserialize(cultureInfo);

        foreach (var encodingInfo in Encoding.GetEncodings())
            SerializeAndDeserialize(encodingInfo.GetEncoding());
    }
    
    [Fact]
    public void BsonSerializerAdapter_should_deserialize_Hashtable_original_content_types()
    {
        var originalHashtable = new Hashtable();
        int originalValue = 10;
        originalHashtable["StoredValue"] = originalValue;
        
        var serializer = new BsonSerializerAdapter();
        var serializedBytes = serializer.Serialize(originalHashtable);
        var deserializedHastable = serializer.Deserialize<Hashtable>(serializedBytes);
        var deserializedValue = deserializedHastable["StoredValue"];
        
        Assert.Equal(originalValue, deserializedValue); 
        Assert.True(deserializedValue is int);
        
        var enumHashtable = new Hashtable();
        var originalEnumValue = Tools.TestEnum.Second;
        enumHashtable["StoredEnum"] = originalEnumValue;
        
        var enumSerializedBytes = serializer.Serialize(enumHashtable);
        var deserializedEnumHashtable = serializer.Deserialize<Hashtable>(enumSerializedBytes);
        var deserializedEnumValue = deserializedEnumHashtable["StoredEnum"];
        
        Assert.Equal(originalEnumValue, deserializedEnumValue);
        Assert.True(deserializedEnumValue is Tools.TestEnum);
    }

    public class CustomClass
    {
        public int SomeProp { get; set; }
    }
    
    [Fact]
    public void BsonSerializerAdapter_should_deserialize_Hashtable_custom_types()
    {
        var ht = new Hashtable { ["CustomClass"] = new CustomClass { SomeProp = 42} };
        
        var serializer = new BsonSerializerAdapter();
        var bytes = serializer.Serialize(ht);
        var desDto = serializer.Deserialize<Hashtable>(bytes);
        
        var originalObj = (CustomClass)ht["CustomClass"];
        var deserializedObj = (CustomClass)desDto["CustomClass"];
    
        Assert.Equal(originalObj.SomeProp, deserializedObj.SomeProp);
    }
    
    [Fact]
    public void BsonSerializerAdapter_should_deserialize_nested_Hashtables()
    {
        var innerHt = new Hashtable { ["value"] = 1 };
        var ht = new Hashtable { ["innerHt"] = innerHt };
        
        var serializer = new BsonSerializerAdapter();
        var bytes = serializer.Serialize(ht);
        var desDto = serializer.Deserialize<Hashtable>(bytes);
        
        var originalObj = (Hashtable)ht["innerHt"];
        var deserializedObj = (Hashtable)desDto["innerHt"];
    
        Assert.Equal((int)originalObj["value"], (int)deserializedObj["value"]);
    }
    
    [Fact]
    public void BsonSerializerAdapter_should_deserialize_Hashtable_that_contains_List()
    {
        var ht = new Hashtable { ["list"] = new List<int>{1} };
        
        var serializer = new BsonSerializerAdapter();
        var bytes = serializer.Serialize(ht);
        var desDto = serializer.Deserialize<Hashtable>(bytes);
        
        var originalObj = (List<int>)ht["list"];
        var deserializedObj = (List<int>)desDto["list"];
    
        Assert.Equal(originalObj.Count, deserializedObj.Count);
    }
    
    private Hashtable BSONSerializeDeserialize(Hashtable testHashtable)
    {
        var serializer = new BsonSerializerAdapter();
        var input = serializer.Serialize(testHashtable);
        var output = serializer.Deserialize<Hashtable>(input);
        return output;
    }
    
    [Fact]
    public void BsonSerializerAdapter_should_deserialize_Hashtable_IntArray()
    {
        int[] typeIntArray = new int[] { 1, 2, 3 };
        var param = new Hashtable();
        param.Add("@objectType", typeIntArray);
        Hashtable desParam = BSONSerializeDeserialize(param);
        Assert.IsType<int[]>(desParam["@objectType"]);
        Assert.Equal(new int[] { 1, 2, 3 }, desParam["@objectType"]);
    }

    [Fact]
    public void BsonSerializerAdapter_should_deserialize_Hashtable_LongArray()
    {
        long[] typeLongArray = new long[] { 1000000000L, 2000000000L };
        var param = new Hashtable();
        param.Add("@objectType", typeLongArray);
        Hashtable desParam = BSONSerializeDeserialize(param);
        Assert.IsType<long[]>(desParam["@objectType"]);
        Assert.Equal(new long[] { 1000000000L, 2000000000L }, desParam["@objectType"]);
    }

    [Fact]
    public void BsonSerializerAdapter_should_deserialize_Hashtable_Object()
    {
        object typeObject = new object();
        var param = new Hashtable();
        param.Add("@objectType", typeObject);
        Hashtable desParam = BSONSerializeDeserialize(param);
        // Empty objects are serialized as JObject in JSON, which is expected behavior
        Assert.IsType<Newtonsoft.Json.Linq.JObject>(desParam["@objectType"]);
    }

    [Fact]
    public void BsonSerializerAdapter_should_deserialize_Hashtable_ObjectArray()
    {
        object[] typeObjectArray = new object[] { "string", 42, true };
        var param = new Hashtable();
        param.Add("@objectType", typeObjectArray);
        Hashtable desParam = BSONSerializeDeserialize(param);
        Assert.IsType<object[]>(desParam["@objectType"]);
        var deserializedArray = (object[])desParam["@objectType"];
        Assert.Equal("string", deserializedArray[0]);
        // BSON converts integers to long, so we need to handle this
        Assert.Equal(42L, deserializedArray[1]);
        Assert.Equal(true, deserializedArray[2]);
    }

    [Fact]
    public void BsonSerializerAdapter_should_deserialize_Hashtable_Hashtable()
    {
        var typeHashtable = new Hashtable();
        typeHashtable.Add("key1", "value1");
        var param = new Hashtable();
        param.Add("@objectType", typeHashtable);
        Hashtable desParam = BSONSerializeDeserialize(param);
        Assert.IsType<Hashtable>(desParam["@objectType"]);
        var innerHt = (Hashtable)desParam["@objectType"];
        Assert.Equal("value1", innerHt["key1"]);
    }

    [Fact]
    public void BsonSerializerAdapter_should_deserialize_Hashtable_EmptyListInt()
    {
        var list = new List<int>();
        var param = new Hashtable();
        param.Add("@listType", list);
        Hashtable desParam = BSONSerializeDeserialize(param);
        Assert.IsType<List<int>>(desParam["@listType"]);
        Assert.Equal(list.Count, ((List<int>)desParam["@listType"]).Count);
    }

    [Fact]
    public void BsonSerializerAdapter_should_deserialize_Hashtable_ListInt()
    {
        var list = new List<int> { 1, 2, 3 };
        var param = new Hashtable();
        param.Add("@listType", list);
        Hashtable desParam = BSONSerializeDeserialize(param);
        Assert.IsType<List<int>>(desParam["@listType"]);
        Assert.Equal(list.Count, ((List<int>)desParam["@listType"]).Count);
        Assert.Equal(list, (List<int>)desParam["@listType"]);
    }

    [Fact]
    public void BsonSerializerAdapter_should_deserialize_Hashtable_NestedHashtableInt()
    {
        var innerHashtable = new Hashtable();
        innerHashtable.Add("key", 42); // int значение
        var outerHashtable = new Hashtable();
        outerHashtable.Add("@innerObject", innerHashtable);
        Hashtable deserializedOuter = BSONSerializeDeserialize(outerHashtable);
        Assert.IsType<Hashtable>(deserializedOuter["@innerObject"]);
        var deserializedInner = (Hashtable)deserializedOuter["@innerObject"];
        Assert.Equal(42, deserializedInner["key"]);
    }

    [Fact]
    public void BsonSerializerAdapter_should_deserialize_Hashtable_NestedHashtableEmptyArray()
    {
        var innerHashtable = new Hashtable();
        innerHashtable.Add("key", new int[0]);
        var outerHashtable = new Hashtable();
        outerHashtable.Add("@innerObject", innerHashtable);
        Hashtable deserializedOuter = BSONSerializeDeserialize(outerHashtable);
        Assert.IsType<Hashtable>(deserializedOuter["@innerObject"]);
        var deserializedInner = (Hashtable)deserializedOuter["@innerObject"];
        Assert.Equal(new int[0], (int[])deserializedInner["key"]);
    }

    [Fact]
    public void BsonSerializerAdapter_should_deserialize_Hashtable_NestedHashtableList()
    {
        var innerHashtable = new Hashtable();
        innerHashtable.Add("key", new List<int>());
        var outerHashtable = new Hashtable();
        outerHashtable.Add("@innerObject", innerHashtable);
        Hashtable deserializedOuter = BSONSerializeDeserialize(outerHashtable);
        Assert.IsType<Hashtable>(deserializedOuter["@innerObject"]);
        var deserializedInner = (Hashtable)deserializedOuter["@innerObject"];
        Assert.Equal(new List<int>(), (List<int>)deserializedInner["key"]);
    }

    [Fact]
    public void BsonSerializerAdapter_should_deserialize_Hashtable_NestedHashtableDictionaryInt()
    {
        var innerHashtable = new Hashtable();
        innerHashtable.Add("key", new Dictionary<string, int>());
        var outerHashtable = new Hashtable();
        outerHashtable.Add("@innerObject", innerHashtable);
        Hashtable deserializedOuter = BSONSerializeDeserialize(outerHashtable);
        Assert.IsType<Hashtable>(deserializedOuter["@innerObject"]);
        var deserializedInner = (Hashtable)deserializedOuter["@innerObject"];
        Assert.Equal(new Dictionary<string, int>(), (Dictionary<string, int>)deserializedInner["key"]);
    }

    [Fact]
    public void BsonSerializerAdapter_should_deserialize_Hashtable_NestedHashtableLong()
    {
        var innerHashtable = new Hashtable();
        innerHashtable.Add("key", 123456789012345L);
        var outerHashtable = new Hashtable();
        outerHashtable.Add("@innerObject", innerHashtable);
        Hashtable deserializedOuter = BSONSerializeDeserialize(outerHashtable);
        Assert.IsType<Hashtable>(deserializedOuter["@innerObject"]);
        var deserializedInner = (Hashtable)deserializedOuter["@innerObject"];
        Assert.Equal(123456789012345L, deserializedInner["key"]);
    }

    [Fact]
    public void BsonSerializerAdapter_should_deserialize_Hashtable_NestedHashtableLongEmptyArray()
    {
        var innerHashtable = new Hashtable();
        innerHashtable.Add("key", new long[0]);
        var outerHashtable = new Hashtable();
        outerHashtable.Add("@innerObject", innerHashtable);
        Hashtable deserializedOuter = BSONSerializeDeserialize(outerHashtable);
        Assert.IsType<Hashtable>(deserializedOuter["@innerObject"]);
        var deserializedInner = (Hashtable)deserializedOuter["@innerObject"];
        Assert.Equal(new long[0], (long[])deserializedInner["key"]);
    }

    [Fact]
    public void BsonSerializerAdapter_should_deserialize_Hashtable_NestedHashtableListLong()
    {
        var innerHashtable = new Hashtable();
        innerHashtable.Add("key", new List<long>());
        var outerHashtable = new Hashtable();
        outerHashtable.Add("@innerObject", innerHashtable);
        Hashtable deserializedOuter = BSONSerializeDeserialize(outerHashtable);
        Assert.IsType<Hashtable>(deserializedOuter["@innerObject"]);
        var deserializedInner = (Hashtable)deserializedOuter["@innerObject"];
        Assert.Equal(new List<long>(), (List<long>)deserializedInner["key"]);
    }

    [Fact]
    public void BsonSerializerAdapter_should_deserialize_Hashtable_NestedHashtableDictionaryStringLong()
    {
        var innerHashtable = new Hashtable();
        innerHashtable.Add("key", new Dictionary<string, long>());
        var outerHashtable = new Hashtable();
        outerHashtable.Add("@innerObject", innerHashtable);
        Hashtable deserializedOuter = BSONSerializeDeserialize(outerHashtable);
        Assert.IsType<Hashtable>(deserializedOuter["@innerObject"]);
        var deserializedInner = (Hashtable)deserializedOuter["@innerObject"];
        Assert.Equal(new Dictionary<string, long>(), (Dictionary<string, long>)deserializedInner["key"]);
    }

    [Fact]
    public void BsonSerializerAdapter_should_deserialize_Hashtable_NestedHashtableString()
    {
        var innerHashtable = new Hashtable();
        innerHashtable.Add("key", "test string");
        var outerHashtable = new Hashtable();
        outerHashtable.Add("@innerObject", innerHashtable);
        Hashtable deserializedOuter = BSONSerializeDeserialize(outerHashtable);
        Assert.IsType<Hashtable>(deserializedOuter["@innerObject"]);
        var deserializedInner = (Hashtable)deserializedOuter["@innerObject"];
        Assert.Equal("test string", deserializedInner["key"]);
    }

    [Fact]
    public void BsonSerializerAdapter_should_deserialize_Hashtable_NestedHashtableStringEmptyArray()
    {
        var innerHashtable = new Hashtable();
        innerHashtable.Add("key", new string[0]);
        var outerHashtable = new Hashtable();
        outerHashtable.Add("@innerObject", innerHashtable);
        Hashtable deserializedOuter = BSONSerializeDeserialize(outerHashtable);
        Assert.IsType<Hashtable>(deserializedOuter["@innerObject"]);
        var deserializedInner = (Hashtable)deserializedOuter["@innerObject"];
        Assert.Equal(new string[0], (string[])deserializedInner["key"]);
    }

    [Fact]
    public void BsonSerializerAdapter_should_deserialize_Hashtable_NestedHashtableListString()
    {
        var innerHashtable = new Hashtable();
        innerHashtable.Add("key", new List<string>());
        var outerHashtable = new Hashtable();
        outerHashtable.Add("@innerObject", innerHashtable);
        Hashtable deserializedOuter = BSONSerializeDeserialize(outerHashtable);
        Assert.IsType<Hashtable>(deserializedOuter["@innerObject"]);
        var deserializedInner = (Hashtable)deserializedOuter["@innerObject"];
        Assert.Equal(new List<string>(), (List<string>)deserializedInner["key"]);
    }

    [Fact]
    public void BsonSerializerAdapter_should_deserialize_Hashtable_NestedHashtableDictionaryString()
    {
        var innerHashtable = new Hashtable();
        innerHashtable.Add("key", new Dictionary<string, string>());
        var outerHashtable = new Hashtable();
        outerHashtable.Add("@innerObject", innerHashtable);
        Hashtable deserializedOuter = BSONSerializeDeserialize(outerHashtable);
        Assert.IsType<Hashtable>(deserializedOuter["@innerObject"]);
        var deserializedInner = (Hashtable)deserializedOuter["@innerObject"];
        Assert.Equal(new Dictionary<string, string>(),
            (Dictionary<string, string>)deserializedInner["key"]);
    }

    [Fact]
    public void BsonSerializerAdapter_should_deserialize_Hashtable_NestedHashtableByteArrayEmpty()
    {
        var innerHashtable = new Hashtable();
        innerHashtable.Add("key", new byte[0]);
        var outerHashtable = new Hashtable();
        outerHashtable.Add("@innerObject", innerHashtable);
        Hashtable deserializedOuter = BSONSerializeDeserialize(outerHashtable);
        Assert.IsType<Hashtable>(deserializedOuter["@innerObject"]);
        var deserializedInner = (Hashtable)deserializedOuter["@innerObject"];
        Assert.Equal(new byte[0], (byte[])deserializedInner["key"]);
    }

    [Fact]
    public void BsonSerializerAdapter_should_deserialize_Hashtable_NestedHashtableByteArray()
    {
        var innerHashtable = new Hashtable();
        innerHashtable.Add("key", new byte[] { 1, 2, 3 });
        var outerHashtable = new Hashtable();
        outerHashtable.Add("@innerObject", innerHashtable);
        Hashtable deserializedOuter = BSONSerializeDeserialize(outerHashtable);
        Assert.IsType<Hashtable>(deserializedOuter["@innerObject"]);
        var deserializedInner = (Hashtable)deserializedOuter["@innerObject"];
        Assert.Equal(new byte[] { 1, 2, 3 }, (byte[])deserializedInner["key"]);
    }

    [Fact]
    public void BsonSerializerAdapter_should_deserialize_Hashtable_NestedHashtableListByteArray()
    {
        var innerHashtable = new Hashtable();
        innerHashtable.Add("key", new List<byte[]>());
        var outerHashtable = new Hashtable();
        outerHashtable.Add("@innerObject", innerHashtable);
        Hashtable deserializedOuter = BSONSerializeDeserialize(outerHashtable);
        Assert.IsType<Hashtable>(deserializedOuter["@innerObject"]);
        var deserializedInner = (Hashtable)deserializedOuter["@innerObject"];
        Assert.Equal(new List<byte[]>(), (List<byte[]>)deserializedInner["key"]);
    }

    [Fact]
    public void BsonSerializerAdapter_should_deserialize_Hashtable_NestedHashtableDictionaryStringByteArray()
    {
        var innerHashtable = new Hashtable();
        innerHashtable.Add("key", new Dictionary<string, byte[]>());
        var outerHashtable = new Hashtable();
        outerHashtable.Add("@innerObject", innerHashtable);
        Hashtable deserializedOuter = BSONSerializeDeserialize(outerHashtable);
        Assert.IsType<Hashtable>(deserializedOuter["@innerObject"]);
        var deserializedInner = (Hashtable)deserializedOuter["@innerObject"];
        Assert.Equal(new Dictionary<string, byte[]>(),
            (Dictionary<string, byte[]>)deserializedInner["key"]);
    }

    [Fact]
    public void BsonSerializerAdapter_should_deserialize_Hashtable_NestedHashtableDateTime()
    {
        var innerHashtable = new Hashtable();
        innerHashtable.Add("key", new DateTime(2023, 10, 1, 12, 30, 45, 500));
        var outerHashtable = new Hashtable();
        outerHashtable.Add("@innerObject", innerHashtable);
        Hashtable deserializedOuter = BSONSerializeDeserialize(outerHashtable);
        Assert.IsType<Hashtable>(deserializedOuter["@innerObject"]);
        var deserializedInner = (Hashtable)deserializedOuter["@innerObject"];
        Assert.Equal(new DateTime(2023, 10, 1, 12, 30, 45, 500), deserializedInner["key"]);
    }

    [Fact]
    public void BsonSerializerAdapter_should_deserialize_Hashtable_TimeSpan()
    {
        var timeSpan = new TimeSpan(0, 5, 0);
        Hashtable dto = new Hashtable { ["TimeSpan"] = timeSpan };
        var desDto = BSONSerializeDeserialize(dto);
        Assert.Equal((TimeSpan)dto["TimeSpan"], (TimeSpan)desDto["TimeSpan"]);
    }
}