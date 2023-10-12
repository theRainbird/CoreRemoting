namespace CoreRemoting.Tests.Tools
{
    public class FactoryService : IFactoryService
    {
        public ITestService GetTestService()
        {
            return new TestService();
        }
    }
}