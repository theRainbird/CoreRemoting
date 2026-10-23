using CoreRemoting.RemoteDelegates;

namespace CoreRemoting.Tests.Tools;

public class EventRelay(IEventDispatcher dispatcher) : IEventRelay
{
    public void RelayTestServiceEvent() =>
        dispatcher.Invoke<ITestService>(s => nameof(s.ServiceEvent));

    public void RelayTestServiceEventByName() =>
        dispatcher.Invoke(typeof(ITestService).FullName, nameof(ITestService.ServiceEvent));
}
