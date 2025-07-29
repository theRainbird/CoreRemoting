using System;
using System.Diagnostics;

namespace CoreRemoting.RemoteDelegates;

/// <summary>
/// Simple dynamic delegate invoker.
/// </summary>
/// <remarks>
/// Features:
/// - Uses late-bound delegate invocation.
/// - Doesn't check delegate type.
/// - Executes multicast delegates sequentially.
/// </remarks>
public class SimpleDynamicInvoker : IDelegateInvoker
{
    /// <inheritdoc/>
    public void Invoke(Delegate handler, object[] arguments)
    {
        try
        {
            handler?.DynamicInvoke(arguments);
        }
        catch (Exception ex)
        {
            Trace.WriteLine("Invocation failed: " + ex.ToString());
        }
    }
}
