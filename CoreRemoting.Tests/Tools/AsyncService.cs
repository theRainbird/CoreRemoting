using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace CoreRemoting.Tests.Tools;

public class AsyncService : IAsyncService
{
    public async Task<string> ConvertToBase64Async(string text)
    {
        var convertFunc = new Func<string>(() =>
        {
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
            return Convert.ToBase64String(stream.ToArray());
        });

        var base64String = await Task.Run(convertFunc);

        return base64String;
    }

    public Task NonGenericTask()
    {
        return Task.CompletedTask;
    }

    public async ValueTask<string> ConvertToBase64ValueTaskAsync(string text)
    {
        var convertFunc = new Func<string>(() =>
        {
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
            return Convert.ToBase64String(stream.ToArray());
        });

        var base64String = await Task.Run(convertFunc);

        return base64String;
    }

    public ValueTask NonGenericValueTask()
    {
        return new ValueTask();
    }
}