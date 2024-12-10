using System;
using System.Linq;
using System.Reflection;
using Castle.MicroKernel.ComponentActivator;

namespace CoreRemoting.Serialization;

/// <summary>
/// Extension methods for the exception classes.
/// </summary>
public static class ExceptionExtensions
{
    /// <summary>
    /// Checks whether the exception is serializable.
    /// </summary>
    public static bool IsSerializable(this Exception ex) => ex switch
    {
        null => true,

        AggregateException agg =>
            agg.InnerExceptions.All(ix => ix.IsSerializable()) &&
                agg.InnerException.IsSerializable() &&
                agg.GetType().IsSerializable,

        // this exception is not deserializable
        ComponentActivatorException cax => false,

        _ => ex.GetType().IsSerializable &&
            ex.InnerException.IsSerializable()
    };

    /// <summary>
    /// Converts the non-serializable exception to a serializable copy.
    /// </summary>
    public static Exception ToSerializable(this Exception ex) =>
        ex.IsSerializable() ? ex :
            new SerializableException(ex.GetType().Name, ex.Message,
                ex.InnerException.ToSerializable(), ex.StackTrace)
                    .CopyDataFrom(ex);

    /// <summary>
    /// Copies all exception data slots from the original exception.
    /// </summary>
    /// <typeparam name="TException">Exception type.</typeparam>
    /// <param name="ex">Target exception.</param>
    /// <param name="original">Original exception.</param>
    /// <returns>Modified target exception.</returns>
    public static TException CopyDataFrom<TException>(this TException ex, Exception original)
        where TException : Exception
    {
        if (ex == null || original == null)
            return ex;

        foreach (var key in original.Data.Keys)
            ex.Data[key] = original.Data[key];

        return ex;
    }

    /// <summary>
    /// Skips the uninformative target invocation exceptions.
    /// </summary>
    /// <param name="ex">The exception to process.</param>
    public static Exception SkipTargetInvocationExceptions(this Exception ex)
    {
        while (ex is TargetInvocationException && ex.InnerException != null)
            ex = ex.InnerException;

        return ex;
    }


    /// <summary>
    /// Returns the most inner exception.
    /// </summary>
    public static Exception GetInnermostException(this Exception ex)
    {
        while (ex?.InnerException != null)
            ex = ex.InnerException;

        return ex;
    }
}
