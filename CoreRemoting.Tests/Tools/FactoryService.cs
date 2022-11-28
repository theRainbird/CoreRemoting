namespace CoreRemoting.Tests.Tools
{
    using CoreRemoting;
    
    public class FactoryService : IFactoryService
    {
        public ITestService GetTestService()
        {
            return new TestService();
        }
    }
}