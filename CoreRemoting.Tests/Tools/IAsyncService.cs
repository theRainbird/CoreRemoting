using System.Threading.Tasks;

namespace CoreRemoting.Tests.Tools;

public interface IAsyncService
{
    Task<string> ConvertToBase64Async(string text);

    Task NonGenericTask();
}