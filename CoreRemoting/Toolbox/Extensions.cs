using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using System.Reflection;
using CoreRemoting.Authentication;

namespace CoreRemoting.Toolbox;

/// <summary>
/// Extension methods.
/// </summary>
public static class Extensions
{
    /// <summary>
    /// Checks whether the given method represents event subscription or unsubscription.
    /// </summary>
    /// <param name="method">Method information.</param>
    /// <param name="eventName">If return value is true, this parameter returns the name of the event.</param>
    /// <param name="subscription">If true, method represents subscription, otherwise, it's unsubscription.</param>
    public static bool IsEventAccessor(this MethodInfo method, out string eventName, out bool subscription)
    {
        // void add_Click(EventHandler e) → subscription to Click
        // void remove_Click(EventHandler e) → unsubscription from Click
        eventName = null;
        subscription = false;

        if (method == null ||
            method.IsGenericMethod ||
            method.ReturnType != typeof(void))
        {
            return false;
        }

        if (method.Name.StartsWith("add_"))
        {
            eventName = method.Name.Substring(4);
            subscription = true;
        }
        else if (method.Name.StartsWith("remove_"))
        {
            eventName = method.Name.Substring(7);
            subscription = false;
        }
        else
        {
            return false;
        }

        var parameters = method.GetParameters();
        if (parameters.Length != 1)
        {
            return false;
        }

        return typeof(Delegate)
            .IsAssignableFrom(parameters[0].ParameterType);
    }

    private static ConcurrentDictionary<Type, object> DefaultValues = new();

    /// <summary>
    /// Gets the default value for the given type.
    /// </summary>
    /// <param name="type">The type.</param>
    /// <returns>default() for the type.</returns>
    public static object GetDefaultValue(this Type type)
    {
        if (type == typeof(void) || !type.IsValueType)
        {
            return null;
        }

        return DefaultValues.GetOrAdd(type, Activator.CreateInstance);
    }

    /// <summary>
    /// Checks if the given type is LINQ expression type.
    /// </summary>
    /// <param name="type">The type.</param>
    /// <returns>True, if it's an expression.</returns>
    public static bool IsLinqExpressionType(this Type type)
    {
        // turns out, this definition is too narrow:
        // var isLinqExpression =
        //    argumentType is
        //    {
        //        IsGenericType: true,
        //        BaseType.IsGenericType: true
        //    }
        //    && argumentType.BaseType.GetGenericTypeDefinition() == typeof(Expression<>);

        return typeof(Expression).IsAssignableFrom(type);
    }

    /// <summary>
    /// Dumps the given byte array as hexadecimal text.
    /// </summary>
    /// <param name="bytes">The array to dump.</param>
    public static string HexDump(this byte[] bytes) => bytes == null ? "" :
        string.Join("\n", Enumerable.Range(0, (bytes.Length + 15) / 16)
            .Select(i => string.Join(" ", bytes.Skip(i * 16).Take(16).Select(b => b.ToString("X2")))));

    /// <summary>
    /// Finds the credential value by name, case-insensitive.
    /// </summary>
    /// <param name="credentials">Credentials array.</param>
    /// <param name="name">Credential name.</param>
    public static string FindByName(this Credential[] credentials, string name) =>
        credentials?.FirstOrDefault(c =>
            string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))?.Value;

    /// <summary>
    /// Finds the parameter value by name, case-insensitive.
    /// </summary>
    /// <param name="parameters">Parameters array.</param>
    /// <param name="name">Credential name.</param>
    public static string FindByName(this AuthenticationResponseParameter[] parameters, string name) =>
        parameters?.FirstOrDefault(c =>
            string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))?.Value;

    /// <summary>
    /// Appends the given values to the array.
    /// </summary>
    /// <typeparam name="T">The type of the element.</typeparam>
    /// <param name="array">Array to append values to.</param>
    /// <param name="values">Values to append.</param>
    public static T[] Append<T>(this T[] array, params T[] values) =>
        (array ?? []).Concat(values ?? []).ToArray();

    /// <summary>
    /// Appends the given credentials to the array.
    /// </summary>
    /// <param name="array">Array to append values to.</param>
    /// <param name="anonymous">Append all properties of the anonymous object.</param>
    public static Credential[] Append(this Credential[] array, object anonymous) =>
        array.Append(anonymous is null ? [] : anonymous.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => new { p, value = p.GetValue(anonymous) })
            .Where(x => x.value != null)
            .Select(x => new Credential { Name = x.p.Name, Value = x.value.ToString() })
            .ToArray());
}
