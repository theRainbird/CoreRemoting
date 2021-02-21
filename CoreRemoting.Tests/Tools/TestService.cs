using System;

namespace CoreRemoting.Tests.Tools
{
    public class TestService : ITestService
    {
        public Func<object, object> TestMethodFake { get; set; }

        public Action OneWayMethodFake { get; set; }
        
        public event Action ServiceEvent; 
        
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
        
        public void OneWayMethod()
        {
            OneWayMethodFake?.Invoke();
        }
    }
}