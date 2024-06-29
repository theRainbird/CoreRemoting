namespace CoreRemoting.Tests.Tools;

public enum TestEnum
{
    First = 1,
    Second = 2
}

public interface IEnumTestService
{
    TestEnum Echo(TestEnum inputValue);

    TestEnum Echo2(TestEnum input)
    {
        return input;
    }
}