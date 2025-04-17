using System;
using System.Data;
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
                new object[] { 4711});

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
        var config = new BsonSerializerConfig(new []
        {
            fakeConverter
        });

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
    public void BsonSerializerAdapter_should_deserialize_TimeSpan_value_in_envelope_correctly()
    {
        var serializerAdapter = new BsonSerializerAdapter();

        var timeSpanToSerialize = TimeSpan.FromSeconds(42);

        var raw = serializerAdapter.Serialize(timeSpanToSerialize);
        var deserializedTimeSpan = serializerAdapter.Deserialize<TimeSpan>(raw);

        Assert.Equal(timeSpanToSerialize, deserializedTimeSpan);
    }

    [Fact]
    public void BsonSerializerAdapter_should_respect_TypeConversionStrategy()
    {
        var serializerAdapter = new BsonSerializerAdapter();

        var uriToSerialize = new Uri("http://127.0.0.1");

        var raw = serializerAdapter.Serialize(uriToSerialize);

        // Convert.ChangeType fails because Uri is serialized as a string
        Assert.Throws<InvalidCastException>(() => serializerAdapter.Deserialize<Uri>(raw));

        var defaultTypeConversionStrategy = Envelope.TypeConversionStrategy;

        // Inject custom logic to handle Uri
        Envelope.TypeConversionStrategy = (value, type) =>
        {
            if (type == typeof(Uri) && value is string stringValue)
                return new Uri(stringValue);

            return Convert.ChangeType(value, type);
        };

        var deserializedUri = serializerAdapter.Deserialize<Uri>(raw);

        Envelope.TypeConversionStrategy = defaultTypeConversionStrategy;

        Assert.Equal(uriToSerialize, deserializedUri);
    }
}