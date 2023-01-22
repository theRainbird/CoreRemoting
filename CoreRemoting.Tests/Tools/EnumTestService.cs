namespace CoreRemoting.Tests.Tools;

public class EnumTestService : IEnumTestService
{
    public TestEnum Echo(TestEnum inputValue)
    {
        return inputValue;
    }
}