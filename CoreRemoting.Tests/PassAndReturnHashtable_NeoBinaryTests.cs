using System;
using System.Collections;
using System.Collections.Generic;
using CoreRemoting.DependencyInjection;
using CoreRemoting.Serialization.NeoBinary;
using Xunit;

namespace CoreRemoting.Tests
{
    public interface IPassAndReturnHtService
    {
        Hashtable GetAndReturnHashtable(Hashtable param);
    }

    public class PassAndReturnHtService : IPassAndReturnHtService
    {
        public Hashtable GetAndReturnHashtable(Hashtable param) => param;
    }

    public class NeoBinaryServerFixture : IDisposable
    {
        private RemotingServer _server;

        public int Port { get; } = 9096;

        public NeoBinaryServerFixture()
        {
            var serverConfig = new ServerConfig
            {
                UniqueServerInstanceName = "NeoBinaryServer",
                IsDefault = false,
                MessageEncryption = false,
                NetworkPort = Port,
                Serializer = new NeoBinarySerializerAdapter(new NeoBinarySerializerConfig
                {
                    AllowUnknownTypes = true
                }),
                RegisterServicesAction = container =>
                {
                    container.RegisterService<IPassAndReturnHtService, PassAndReturnHtService>(ServiceLifetime.Singleton);
                }
            };

            _server = new RemotingServer(serverConfig);
            _server.Start();
        }

        public void Dispose()
        {
            _server?.Dispose();
            _server = null;
        }
    }

    public class NeoBinaryClient : IDisposable
    {
        private RemotingClient _client;
        private IPassAndReturnHtService _proxy;

        public NeoBinaryClient(int port)
        {
            _client = new RemotingClient(new ClientConfig
            {
                ServerPort = port,
                MessageEncryption = false,
                Serializer = new NeoBinarySerializerAdapter(new NeoBinarySerializerConfig
                {
                    AllowUnknownTypes = true
                })
            });

            _client.Connect();
            _proxy = _client.CreateProxy<IPassAndReturnHtService>();
        }

        public Hashtable SendAndReceiveHashtable(Hashtable testParam)
        {
            return _proxy.GetAndReturnHashtable(testParam);
        }

        public void Dispose()
        {
            _client?.Dispose();
            _client = null;
            _proxy = null;
        }
    }

    [CollectionDefinition("NeoBinary_Hashtable_Tests")]
    public class NeoBinaryTestCollection : ICollectionFixture<NeoBinaryServerFixture>
    {
    }

    [Collection("NeoBinary_Hashtable_Tests")]
    public class PassAndReturnHashtable_NeoBinaryTests
    {
        private readonly NeoBinaryServerFixture _serverFixture;

        public PassAndReturnHashtable_NeoBinaryTests(NeoBinaryServerFixture serverFixture)
        {
            _serverFixture = serverFixture;
        }

        private Hashtable Roundtrip(Hashtable input)
        {
            using var client = new NeoBinaryClient(_serverFixture.Port);
            return client.SendAndReceiveHashtable(input);
        }

        [Fact]
        public void HashtableBoolServ()
        {
            bool typeBool = true;
            var param = new Hashtable { ["@objectType"] = typeBool };
            Hashtable desParam = Roundtrip(param);
            Assert.IsType<bool>(desParam["@objectType"]);
            Assert.Equal(true, desParam["@objectType"]);
        }

        [Fact]
        public void HashtableByteServ()
        {
            byte typeByte = 42;
            var param = new Hashtable { ["@objectType"] = typeByte };
            Hashtable desParam = Roundtrip(param);
            Assert.IsType<byte>(desParam["@objectType"]);
            Assert.Equal((byte)42, desParam["@objectType"]);
        }

        [Fact]
        public void HashtableByteArrayServ()
        {
            byte[] typeByteArray = new byte[] { 42, 43, 44 };
            var param = new Hashtable { ["@objectType"] = typeByteArray };
            Hashtable desParam = Roundtrip(param);
            Assert.IsType<byte[]>(desParam["@objectType"]);
            Assert.Equal(new byte[] { 42, 43, 44 }, (byte[])desParam["@objectType"]);
        }

        [Fact]
        public void HashtableIntServ()
        {
            int typeInt = 42;
            var param = new Hashtable { ["@objectType"] = typeInt };
            Hashtable desParam = Roundtrip(param);
            Assert.IsType<int>(desParam["@objectType"]);
            Assert.Equal(42, desParam["@objectType"]);
        }

        [Fact]
        public void HashtableIntArrayServ()
        {
            int[] typeIntArray = new int[] { 1, 2, 3 };
            var param = new Hashtable { ["@objectType"] = typeIntArray };
            Hashtable desParam = Roundtrip(param);
            Assert.IsType<int[]>(desParam["@objectType"]);
            Assert.Equal(new int[] { 1, 2, 3 }, desParam["@objectType"]);
        }

        [Fact]
        public void HashtableLongServ()
        {
            long typeLong = 123456789012345L;
            var param = new Hashtable { ["@objectType"] = typeLong };
            Hashtable desParam = Roundtrip(param);
            Assert.IsType<long>(desParam["@objectType"]);
            Assert.Equal(123456789012345L, desParam["@objectType"]);
        }

        [Fact]
        public void HashtableLongArrayServ()
        {
            long[] typeLongArray = new long[] { 1000000000L, 2000000000L };
            var param = new Hashtable { ["@objectType"] = typeLongArray };
            Hashtable desParam = Roundtrip(param);
            Assert.IsType<long[]>(desParam["@objectType"]);
            Assert.Equal(new long[] { 1000000000L, 2000000000L }, desParam["@objectType"]);
        }

        [Fact]
        public void HashtableFloatServ()
        {
            float typeFloat = 3.14f;
            var param = new Hashtable { ["@objectType"] = typeFloat };
            Hashtable desParam = Roundtrip(param);
            Assert.IsType<float>(desParam["@objectType"]);
            Assert.Equal(3.14f, desParam["@objectType"]);
        }

        [Fact]
        public void HashtableObjectServ()
        {
            object typeObject = new object();
            var param = new Hashtable { ["@objectType"] = typeObject };
            Hashtable desParam = Roundtrip(param);
            Assert.IsType<object>(desParam["@objectType"]);
        }

        [Fact]
        public void HashtableObjectArrayServ()
        {
            object[] typeObjectArray = new object[] { "string", 42, true };
            var param = new Hashtable { ["@objectType"] = typeObjectArray };
            Hashtable desParam = Roundtrip(param);
            Assert.IsType<object[]>(desParam["@objectType"]);
            Assert.Equal(new object[] { "string", 42, true }, desParam["@objectType"] as object[]);
        }

        [Fact]
        public void HashtableDateTimeServ()
        {
            DateTime typeDateTime = new DateTime(2023, 10, 1, 12, 0, 0);
            var param = new Hashtable { ["@objectType"] = typeDateTime };
            Hashtable desParam = Roundtrip(param);
            Assert.IsType<DateTime>(desParam["@objectType"]);
            Assert.Equal(new DateTime(2023, 10, 1, 12, 0, 0), desParam["@objectType"]);
        }

        [Fact]
        public void HashtableHashtableServ()
        {
            var typeHashtable = new Hashtable { ["key1"] = "value1" };
            var param = new Hashtable { ["@objectType"] = typeHashtable };
            Hashtable desParam = Roundtrip(param);
            Assert.IsType<Hashtable>(desParam["@objectType"]);
            var innerHt = (Hashtable)desParam["@objectType"];
            Assert.Equal("value1", innerHt["key1"]);
        }

        [Fact]
        public void HashtableCharServ()
        {
            char typeChar = 'A';
            var param = new Hashtable { ["@objectType"] = typeChar };
            Hashtable desParam = Roundtrip(param);
            Assert.IsType<char>(desParam["@objectType"]);
            Assert.Equal('A', desParam["@objectType"]);
        }

        [Fact]
        public void HashtableNullServ()
        {
            object typeNull = null;
            var param = new Hashtable { ["@objectType"] = typeNull };
            Hashtable desParam = Roundtrip(param);
            Assert.Null(desParam["@objectType"]);
        }

        [Fact]
        public void HashTableEmptyListIntServ()
        {
            var list = new List<int>();
            var param = new Hashtable { ["@listType"] = list };
            Hashtable desParam = Roundtrip(param);
            Assert.IsType<List<int>>(desParam["@listType"]);
            Assert.Equal(list.Count, ((List<int>)desParam["@listType"]).Count);
        }

        [Fact]
        public void HashTableListIntServ()
        {
            var list = new List<int> { 1, 2, 3 };
            var param = new Hashtable { ["@listType"] = list };
            Hashtable desParam = Roundtrip(param);
            Assert.IsType<List<int>>(desParam["@listType"]);
            Assert.Equal(list.Count, ((List<int>)desParam["@listType"]).Count);
            Assert.Equal(list, (List<int>)desParam["@listType"]);
        }

        [Fact]
        public void NestedHashtableIntTestServ()
        {
            var innerHashtable = new Hashtable { ["key"] = 42 };
            var outerHashtable = new Hashtable { ["@innerObject"] = innerHashtable };
            Hashtable deserializedOuter = Roundtrip(outerHashtable);
            Assert.IsType<Hashtable>(deserializedOuter["@innerObject"]);
            var deserializedInner = (Hashtable)deserializedOuter["@innerObject"];
            Assert.Equal(42, deserializedInner["key"]);
        }

        [Fact]
        public void NestedHashtableEmptyArrayTestServ()
        {
            var innerHashtable = new Hashtable { ["key"] = new int[0] };
            var outerHashtable = new Hashtable { ["@innerObject"] = innerHashtable };
            Hashtable deserializedOuter = Roundtrip(outerHashtable);
            Assert.IsType<Hashtable>(deserializedOuter["@innerObject"]);
            var deserializedInner = (Hashtable)deserializedOuter["@innerObject"];
            Assert.Equal(new int[0], (int[])deserializedInner["key"]);
        }

        [Fact]
        public void NestedHashtableListTestServ()
        {
            var innerHashtable = new Hashtable { ["key"] = new List<int>() };
            var outerHashtable = new Hashtable { ["@innerObject"] = innerHashtable };
            Hashtable deserializedOuter = Roundtrip(outerHashtable);
            Assert.IsType<Hashtable>(deserializedOuter["@innerObject"]);
            var deserializedInner = (Hashtable)deserializedOuter["@innerObject"];
            Assert.Equal(new List<int>(), (List<int>)deserializedInner["key"]);
        }

        [Fact]
        public void NestedHashtableDictionaryIntTestServ()
        {
            var innerHashtable = new Hashtable { ["key"] = new Dictionary<string, int>() };
            var outerHashtable = new Hashtable { ["@innerObject"] = innerHashtable };
            Hashtable deserializedOuter = Roundtrip(outerHashtable);
            Assert.IsType<Hashtable>(deserializedOuter["@innerObject"]);
            var deserializedInner = (Hashtable)deserializedOuter["@innerObject"];
            Assert.Equal(new Dictionary<string, int>(), (Dictionary<string, int>)deserializedInner["key"]);
        }

        [Fact]
        public void NestedHashtableLongTestServ()
        {
            var innerHashtable = new Hashtable { ["key"] = 123456789012345L };
            var outerHashtable = new Hashtable { ["@innerObject"] = innerHashtable };
            Hashtable deserializedOuter = Roundtrip(outerHashtable);
            Assert.IsType<Hashtable>(deserializedOuter["@innerObject"]);
            var deserializedInner = (Hashtable)deserializedOuter["@innerObject"];
            Assert.Equal(123456789012345L, deserializedInner["key"]);
        }

        [Fact]
        public void NestedHashtableLongEmptyArrayTestServ()
        {
            var innerHashtable = new Hashtable { ["key"] = new long[0] };
            var outerHashtable = new Hashtable { ["@innerObject"] = innerHashtable };
            Hashtable deserializedOuter = Roundtrip(outerHashtable);
            Assert.IsType<Hashtable>(deserializedOuter["@innerObject"]);
            var deserializedInner = (Hashtable)deserializedOuter["@innerObject"];
            Assert.Equal(new long[0], (long[])deserializedInner["key"]);
        }

        [Fact]
        public void NestedHashtableListLongTestServ()
        {
            var innerHashtable = new Hashtable { ["key"] = new List<long>() };
            var outerHashtable = new Hashtable { ["@innerObject"] = innerHashtable };
            Hashtable deserializedOuter = Roundtrip(outerHashtable);
            Assert.IsType<Hashtable>(deserializedOuter["@innerObject"]);
            var deserializedInner = (Hashtable)deserializedOuter["@innerObject"];
            Assert.Equal(new List<long>(), (List<long>)deserializedInner["key"]);
        }

        [Fact]
        public void NestedHashtableDictionaryStringLongTestServ()
        {
            var innerHashtable = new Hashtable { ["key"] = new Dictionary<string, long>() };
            var outerHashtable = new Hashtable { ["@innerObject"] = innerHashtable };
            Hashtable deserializedOuter = Roundtrip(outerHashtable);
            Assert.IsType<Hashtable>(deserializedOuter["@innerObject"]);
            var deserializedInner = (Hashtable)deserializedOuter["@innerObject"];
            Assert.Equal(new Dictionary<string, long>(), (Dictionary<string, long>)deserializedInner["key"]);
        }

        [Fact]
        public void NestedHashtableStringTestServ()
        {
            var innerHashtable = new Hashtable { ["key"] = "test string" };
            var outerHashtable = new Hashtable { ["@innerObject"] = innerHashtable };
            Hashtable deserializedOuter = Roundtrip(outerHashtable);
            Assert.IsType<Hashtable>(deserializedOuter["@innerObject"]);
            var deserializedInner = (Hashtable)deserializedOuter["@innerObject"];
            Assert.Equal("test string", deserializedInner["key"]);
        }

        [Fact]
        public void NestedHashtableStringEmptyArrayTestServ()
        {
            var innerHashtable = new Hashtable { ["key"] = new string[0] };
            var outerHashtable = new Hashtable { ["@innerObject"] = innerHashtable };
            Hashtable deserializedOuter = Roundtrip(outerHashtable);
            Assert.IsType<Hashtable>(deserializedOuter["@innerObject"]);
            var deserializedInner = (Hashtable)deserializedOuter["@innerObject"];
            Assert.Equal(new string[0], (string[])deserializedInner["key"]);
        }

        [Fact]
        public void NestedHashtableListStringTestServ()
        {
            var innerHashtable = new Hashtable { ["key"] = new List<string>() };
            var outerHashtable = new Hashtable { ["@innerObject"] = innerHashtable };
            Hashtable deserializedOuter = Roundtrip(outerHashtable);
            Assert.IsType<Hashtable>(deserializedOuter["@innerObject"]);
            var deserializedInner = (Hashtable)deserializedOuter["@innerObject"];
            Assert.Equal(new List<string>(), (List<string>)deserializedInner["key"]);
        }

        [Fact]
        public void NestedHashtableDictionaryStringTestServ()
        {
            var innerHashtable = new Hashtable { ["key"] = new Dictionary<string, string>() };
            var outerHashtable = new Hashtable { ["@innerObject"] = innerHashtable };
            Hashtable deserializedOuter = Roundtrip(outerHashtable);
            Assert.IsType<Hashtable>(deserializedOuter["@innerObject"]);
            var deserializedInner = (Hashtable)deserializedOuter["@innerObject"];
            Assert.Equal(new Dictionary<string, string>(), (Dictionary<string, string>)deserializedInner["key"]);
        }

        [Fact]
        public void NestedHashtableByteArrayEmptyTestServ()
        {
            var innerHashtable = new Hashtable { ["key"] = new byte[0] };
            var outerHashtable = new Hashtable { ["@innerObject"] = innerHashtable };
            Hashtable deserializedOuter = Roundtrip(outerHashtable);
            Assert.IsType<Hashtable>(deserializedOuter["@innerObject"]);
            var deserializedInner = (Hashtable)deserializedOuter["@innerObject"];
            Assert.Equal(new byte[0], (byte[])deserializedInner["key"]);
        }

        [Fact]
        public void NestedHashtableByteArrayTestServ()
        {
            var innerHashtable = new Hashtable { ["key"] = new byte[] { 1, 2, 3 } };
            var outerHashtable = new Hashtable { ["@innerObject"] = innerHashtable };
            Hashtable deserializedOuter = Roundtrip(outerHashtable);
            Assert.IsType<Hashtable>(deserializedOuter["@innerObject"]);
            var deserializedInner = (Hashtable)deserializedOuter["@innerObject"];
            Assert.Equal(new byte[] { 1, 2, 3 }, (byte[])deserializedInner["key"]);
        }

        [Fact]
        public void NestedHashtableListByteArrayTestServ()
        {
            var innerHashtable = new Hashtable { ["key"] = new List<byte[]>() };
            var outerHashtable = new Hashtable { ["@innerObject"] = innerHashtable };
            Hashtable deserializedOuter = Roundtrip(outerHashtable);
            Assert.IsType<Hashtable>(deserializedOuter["@innerObject"]);
            var deserializedInner = (Hashtable)deserializedOuter["@innerObject"];
            Assert.Equal(new List<byte[]>(), (List<byte[]>)deserializedInner["key"]);
        }

        [Fact]
        public void NestedHashtableDictionaryStringByteArrayTestServ()
        {
            var innerHashtable = new Hashtable { ["key"] = new Dictionary<string, byte[]>() };
            var outerHashtable = new Hashtable { ["@innerObject"] = innerHashtable };
            Hashtable deserializedOuter = Roundtrip(outerHashtable);
            Assert.IsType<Hashtable>(deserializedOuter["@innerObject"]);
            var deserializedInner = (Hashtable)deserializedOuter["@innerObject"];
            Assert.Equal(new Dictionary<string, byte[]>(), (Dictionary<string, byte[]>)deserializedInner["key"]);
        }

        [Fact]
        public void NestedHashtableDateTimeTestServ()
        {
            var innerHashtable = new Hashtable { ["key"] = new DateTime(2023, 10, 1, 12, 30, 45, 500) };
            var outerHashtable = new Hashtable { ["@innerObject"] = innerHashtable };
            Hashtable deserializedOuter = Roundtrip(outerHashtable);
            Assert.IsType<Hashtable>(deserializedOuter["@innerObject"]);
            var deserializedInner = (Hashtable)deserializedOuter["@innerObject"];
            Assert.Equal(new DateTime(2023, 10, 1, 12, 30, 45, 500), deserializedInner["key"]);
        }

        [Fact]
        public void TestTimeSpanServ()
        {
            var timeSpan = new TimeSpan(0, 5, 0);
            Hashtable dto = new Hashtable { ["TimeSpan"] = timeSpan };
            var desDto = Roundtrip(dto);
            Assert.Equal((TimeSpan)dto["TimeSpan"], (TimeSpan)desDto["TimeSpan"]);
        }

        [Serializable]
        private class MyClass
        {
            public bool MyVal = true;
        }

        [Fact]
        public void TestCustomTypeNeoBinarySerializerAdapter()
        {
            Hashtable dto = new Hashtable { ["MyClass"] = new MyClass() };
            var desDto = Roundtrip(dto);
            var originalObj = (MyClass)dto["MyClass"];
            var deserializedObj = (MyClass)desDto["MyClass"];
            Assert.Equal(originalObj.MyVal, deserializedObj.MyVal);
        }

        [Fact]
        public void HashTableDeserializeTestServ()
        {
            var dto = new Hashtable { ["OutputType"] = (byte)1 };
            var desDto = Roundtrip(dto);
            Assert.Equal(dto["OutputType"], desDto["OutputType"]);
        }
    }
}
