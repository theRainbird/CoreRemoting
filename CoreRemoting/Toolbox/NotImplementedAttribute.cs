using System;

namespace CoreRemoting.Toolbox;

/// <summary>
/// Marks a partial class for automatic generation of interface members
/// that throw <see cref="NotImplementedException"/>.
/// Also marks generated members themselves.
/// </summary>
[AttributeUsage(NotImplementedAttribute.Targets, Inherited = false, AllowMultiple = false)]
public sealed class NotImplementedAttribute : Attribute
{
    /// <summary>
    /// Valid targets for this attribute: classes, methods, properties, and events.
    /// </summary>
    public const AttributeTargets Targets =
        AttributeTargets.Class |
        AttributeTargets.Method |
        AttributeTargets.Property |
        AttributeTargets.Event;
}
