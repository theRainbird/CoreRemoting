using System;

namespace CoreRemoting.RemoteDelegates;

/// <summary>
/// Simple dynamic delegate invoker.
/// </summary>
/// <remarks>
/// Features:
/// - Uses late-bound delegate invocation.
/// - Doesn't check delegate type.
/// - Doesn't catch and rethrow exceptions.
/// - Executes multicast delegates sequentially.
/// </remarks>
public class SimpleDynamicInvoker : IDelegateInvoker
{
    /// <inheritdoc/>
    public void Invoke(Delegate handler, object[] arguments)
    {
        handler?.DynamicInvoke(arguments);
    }
}
