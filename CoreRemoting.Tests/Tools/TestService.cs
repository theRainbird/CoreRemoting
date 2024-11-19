using System;
using System.Threading.Tasks;
using CoreRemoting.Tests.ExternalTypes;

namespace CoreRemoting.Tests.Tools
{
    public class TestService : ITestService
    {
        private int _counter;
        
        public Func<object, object> TestMethodFake { get; set; }

        public Action OneWayMethodFake { get; set; }
        
        public Action<DataClass> TestExternalTypeParameterFake { get; set; }
        
        public event Action ServiceEvent; 
        
        public event ServerEventHandler CustomDelegateEvent;
        
        public object TestMethod(object arg)
        {
            return TestMethodFake?.Invoke(arg);
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
            await Task.Delay(1);
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
    }
}