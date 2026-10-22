using System.Diagnostics;
using System.Threading;

namespace CoreRemoting.Threading;

/// <summary>
/// Thread-safe boolean flag.
/// All reads and writes are performed with acquire-release semantics.
/// </summary>
/// <remarks>
/// This is a mutable value type. Always use it as a field, never copy
/// it into a local variable: methods called on a copy will not affect
/// the original field.
/// </remarks>
[DebuggerDisplay("{Value}")]
public struct AtomicBool
{
    private int _value;

    /// <summary>
    /// Gets a value indicating whether the flag is set.
    /// </summary>
    public bool Value => Volatile.Read(ref _value) != 0;

    /// <summary>
    /// Sets the flag to <c>true</c>.
    /// </summary>
    public void Set() => Interlocked.Exchange(ref _value, 1);

    /// <summary>
    /// Sets the flag to <c>true</c> and returns <c>true</c>
    /// if it was <c>false</c> before, otherwise returns <c>false</c>.
    /// </summary>
    public bool TrySet() => Interlocked.Exchange(ref _value, 1) == 0;

    /// <summary>
    /// Sets the flag to <c>false</c>.
    /// </summary>
    public void Reset() => Interlocked.Exchange(ref _value, 0);

    /// <summary>
    /// Sets the flag to <c>false</c> and returns <c>true</c>
    /// if it was <c>true</c> before, otherwise returns <c>false</c>.
    /// </summary>
    public bool TryReset() => Interlocked.Exchange(ref _value, 0) == 1;
}