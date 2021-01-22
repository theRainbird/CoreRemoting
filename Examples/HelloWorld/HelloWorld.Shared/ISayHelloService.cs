using System;

namespace HelloWorld.Shared
{
    public interface ISayHelloService
    {
        event Action<string, string> MessageReceived;
        
        void Say(string name, string message);
    }
}