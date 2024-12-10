using System;
using System.Threading.Tasks;

namespace CoreRemoting.Toolbox;

/// <summary>
/// Helper methods for tasks.
/// </summary>
public static class TaskExtensions
{
    /// <summary>
    /// Ensures that the given task is executed within the specified timeout.
    /// </summary>
    public static async Task<T> Timeout<T>(this Task<T> task, double secTimeout, string message = "Timed out")
    {
        if (secTimeout <= 0)
            return await task;

        var delay = Task.Delay(TimeSpan.FromSeconds(secTimeout));
        var result = await Task.WhenAny(task, delay);
        if (result == task)
            return await task;

        throw new TimeoutException(message);
    }

    /// <summary>
    /// Ensures that the given task is executed within the specified timeout.
    /// </summary>
    public static async Task Timeout(this Task task, double secTimeout, string message = "Timed out")
    {
        if (secTimeout <= 0)
        {
            await task;
            return;
        }

        var delay = Task.Delay(TimeSpan.FromSeconds(secTimeout));
        var result = await Task.WhenAny(task, delay);
        if (result == task)
        {
            await task;
            return;
        }

        throw new TimeoutException(message);
    }
}
