using System;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreRemoting.Tests.ExternalTypes;

namespace CoreRemoting.Tests.Tools;

public class TestService : ITestService
{

    private int _counter;

    public Func<object, object> TestMethodFake { get; set; }

    public Action OneWayMethodFake { get; set; }

    public Action<DataClass> TestExternalTypeParameterFake { get; set; }

    public event Action ServiceEvent;

    public event ServerEventHandler CustomDelegateEvent;

    public event EventHandler<HeavyweightObjectSimulator> HeavyEvent;

    public object TestMethod(object arg)
    {
        return TestMethodFake?.Invoke(arg);
    }

    public object LongRunnigTestMethod(int timeout)
    {
        Thread.Sleep(timeout);
        return TestMethodFake?.Invoke(timeout);
    }

    public void TestMethodWithDelegateArg(Action<string> callback)
    {
        callback("test");
    }

    public void FireServiceEvent()
    {
        ServiceEvent?.Invoke();
    }

    public void FireCustomDelegateEvent()
    {
        CustomDelegateEvent?.Invoke(null);
    }

    public int FireHeavyEvents(params int[] delays)
    {
        foreach (var delay in delays)
        {
            HeavyEvent?.Invoke(null, new() { SerializationDelay = delay });
        }

        return delays.Length;
    }

    public void OneWayMethod()
    {
        OneWayMethodFake?.Invoke();
    }

    public void TestExternalTypeParameter(DataClass data)
    {
        TestExternalTypeParameterFake?.Invoke(data);
    }

    public string Echo(string text)
    {
        return text;
    }

    public string Reverse(string text)
    {
        return new string(text.Reverse().ToArray());
    }

    public void MethodWithOutParameter(out int counter)
    {
        _counter++;
        counter = _counter;
    }

    public bool BaseMethod()
    {
        return true;
    }

    public void Error(string text)
    {
        throw new Exception(text);
    }

    public async Task ErrorAsync(string text)
    {
        await Task.Delay(1).ConfigureAwait(false);
        Error(text);
    }

    private class NonSerializable : Exception
    {
        public NonSerializable(string message)
            : base(message)
        {
        }
    }

    public void NonSerializableError(string text, params object[] data)
    {
        var ex = new NonSerializable(text);

        foreach (var item in data)
            ex.Data[item] = item;

        throw ex;
    }

    public class NonSerializableObject(string text)
    {
        public string Text => throw new Exception(text);
    }

    public object NonSerializableReturnValue(string text)
    {
        return new NonSerializableObject(text);
    }

    public DataTable TestDt(DataTable dt, long num)
    {
        dt.Rows.Clear();
        return dt;
    }

    public (T, int) Duplicate<T>(T sample) where T : class
    {
        return sample switch
        {
            byte[] arr => (Dup(arr) as T, arr.Length * 2),
            int[] iarr => (Dup(iarr) as T, iarr.Length * 2 * sizeof(int)),
            string str => ((str + str) as T, str.Length * 2 * sizeof(char)),
            _ => throw new ArgumentOutOfRangeException(),
        };

        TItem[] Dup<TItem>(TItem[] arr)
        {
            var length = arr.Length;
            Array.Resize(ref arr, length * 2);
            Array.Copy(arr, 0, arr, length, length);
            return arr;
        }
    }

    private static TestService LastInstance { get; set; }

    public void SaveLastInstance() => LastInstance = this;

    public bool CheckLastSavedInstance() => LastInstance == this;

}