using System;
using System.Data;
using System.Threading.Tasks;
using CoreRemoting.Tests.ExternalTypes;

namespace CoreRemoting.Tests.Tools;

public delegate void ServerEventHandler(object sender);

[ReturnAsProxy]
public interface ITestService : IBaseService
{
    event Action ServiceEvent;

    event ServerEventHandler CustomDelegateEvent;

    event EventHandler<HeavyweightObjectSimulator> HeavyEvent;

    object TestMethod(object arg);

    object LongRunnigTestMethod(int timeout);

    void TestMethodWithDelegateArg(Action<string> callback);

    void FireServiceEvent();

    void FireCustomDelegateEvent();

    int FireHeavyEvents(params int[] delays);

    [OneWay]
    void OneWayMethod();

    void TestExternalTypeParameter(DataClass data);

    string Echo(string text);

    string Reverse(string text);

    void MethodWithOutParameter(out int counter);

    void Error(string text);

    Task ErrorAsync(string text);

    void NonSerializableError(string text, params object[] data);

    object NonSerializableReturnValue(string text);

    DataTable TestDt(DataTable dt, long num);

    (T duplicate, int size) Duplicate<T>(T sample) where T : class;

    void SaveLastInstance();

    bool CheckLastSavedInstance();

    ComplexEchoObject EchoComplex(ComplexEchoObject obj);
}