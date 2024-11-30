using System;
using System.Data;
using System.Threading.Tasks;
using CoreRemoting.Tests.ExternalTypes;

namespace CoreRemoting.Tests.Tools
{
    public delegate void ServerEventHandler(object sender);
    
    [ReturnAsProxy]
    public interface ITestService : IBaseService
    {
        event Action ServiceEvent;

        event ServerEventHandler CustomDelegateEvent;
        
        object TestMethod(object arg);

        void TestMethodWithDelegateArg(Action<string> callback);

        void FireServiceEvent();

        void FireCustomDelegateEvent();

        [OneWay]
        void OneWayMethod();

        void TestExternalTypeParameter(DataClass data);

        string Echo(string text);

        void MethodWithOutParameter(out int counter);

        void Error(string text);

        Task ErrorAsync(string text);

        void NonSerializableError(string text, params object[] data);

        DataTable TestDt(DataTable dt, long num);

        (T duplicate, int size) Duplicate<T>(T sample) where T : class;
    }
}