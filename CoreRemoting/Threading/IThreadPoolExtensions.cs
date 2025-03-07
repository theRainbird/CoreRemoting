using System.Threading;

namespace CoreRemoting.Threading;

/// <summary>
/// Extension methods for the <see cref="IThreadPool"/> interface.
/// </summary>
public static class IThreadPoolExtensions
{
    /// <summary>
    /// Queues a method for the execution, and specifies an object to be used by the method.
    /// </summary>
    /// <param name="threadPool">An instance of the <see cref="IThreadPool"/>.</param>
    /// <param name="work">A <see cref="WaitCallback"/> representing the method to execute.</param>
    public static void QueueUserWorkItem(this IThreadPool threadPool, WaitCallback work)
    {
        threadPool.QueueUserWorkItem(work, null);
    }
}
