using System;
using System.Threading.Tasks;

namespace CoreRemoting.Toolbox;

/// <summary>
/// Helper methods for tasks.
/// </summary>
public static class TaskExtensions
{
    private const string TimedOut = "Timed out";

    /// <summary>
    /// Ensures that the given task is executed within the specified timeout.
    /// Throws a <see cref="TimeoutException"/> if failed.
    /// </summary>
    public static Task<T> Timeout<T>(this Task<T> task, double secTimeout, string message = TimedOut) =>
        task.Timeout(secTimeout, () => throw new TimeoutException(message));

    /// <summary>
    /// Ensures that the given task is executed within the specified timeout.
    /// Executes a <paramref name="throwAction"/> callback if failed.
    /// </summary>
    public static async Task<T> Timeout<T>(this Task<T> task, double secTimeout, Action throwAction)
    {
        if (secTimeout <= 0)
            return await task.ConfigureAwait(false);

        var delay = Task.Delay(TimeSpan.FromSeconds(secTimeout));
        var result = await Task.WhenAny(task, delay).ConfigureAwait(false);
        if (result == task)
            return await task.ConfigureAwait(false);

        throwAction?.Invoke();
        throw new TimeoutException(TimedOut);
    }

    /// <summary>
    /// Ensures that the given task is executed within the specified timeout.
    /// Throws a <see cref="TimeoutException"/> if failed.
    /// </summary>
    public static Task Timeout(this Task task, double secTimeout, string message = TimedOut) =>
        task.Timeout(secTimeout, () => throw new TimeoutException(message));

    /// <summary>
    /// Ensures that the given task is executed within the specified timeout.
    /// Executes a <paramref name="throwAction"/> callback if failed.
    /// </summary>
    public static async Task Timeout(this Task task, double secTimeout, Action throwAction)
    {
        if (secTimeout <= 0)
        {
            await task.ConfigureAwait(false);
            return;
        }

        var delay = Task.Delay(TimeSpan.FromSeconds(secTimeout));
        var result = await Task.WhenAny(task, delay).ConfigureAwait(false);
        if (result == task)
        {
            await task.ConfigureAwait(false);
            return;
        }

        throwAction?.Invoke();
        throw new TimeoutException(TimedOut);
    }

    /// <summary>
    /// Ensures that the given task is either executed within the specified timeout or skipped.
    /// </summary>
    public static async Task<bool> Expire(this Task task, double secTimeout)
    {
        if (secTimeout <= 0)
        {
            await task.ConfigureAwait(false);
            return true;
        }

        var delay = Task.Delay(TimeSpan.FromSeconds(secTimeout));
        var result = await Task.WhenAny(task, delay).ConfigureAwait(false);
        if (result == task)
        {
            await task.ConfigureAwait(false);
            return true;
        }

        // note: the task may still be active, but its result is ignored
        return false;
    }

    /// <summary>
    /// Ensures that the given task is either executed within the specified timeout or skipped.
    /// </summary>
    public static Task<bool> ExpireMs(this Task task, int milliseconds) =>
        task.Expire(milliseconds / 1000.0);

    /// <summary>
    /// Waits for the task to complete, but doesn't wrap
    /// the exception in an <see cref="AggregateException"/>.
    /// </summary>
    public static void JustWait(this Task task) =>
        task.GetAwaiter().GetResult();

    /// <summary>
    /// Waits for the task to complete, but doesn't wrap
    /// the exception in an <see cref="AggregateException"/>.
    /// </summary>
    public static void JustWait<T>(this Task<T> task) =>
        task.GetAwaiter().GetResult();

    /// <summary>
    /// Waits for the task to complete, but doesn't wrap
    /// the exception in an <see cref="AggregateException"/>.
    /// </summary>
    public static void JustWait(this ValueTask task) =>
        task.GetAwaiter().GetResult();

    /// <summary>
    /// Waits for the task to complete, but doesn't wrap
    /// the exception in an <see cref="AggregateException"/>.
    /// </summary>
    public static void JustWait<T>(this ValueTask<T> task) =>
        task.GetAwaiter().GetResult();

}
