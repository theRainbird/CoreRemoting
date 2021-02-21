using System;

namespace CoreRemoting.Tests.Tools
{
    public interface ITestService
    {
        event Action ServiceEvent;
        
        object TestMethod(object arg);

        void TestMethodWithDelegateArg(Action<string> callback);

        void FireServiceEvent();

        [OneWay]
        void OneWayMethod();
    }
}