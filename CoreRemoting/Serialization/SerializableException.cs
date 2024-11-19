using System;
using System.Runtime.Serialization;

namespace CoreRemoting.Serialization;

/// <summary>
/// Serializable exception replacement for non-serializable exceptions.
/// </summary>
[Serializable]
public class SerializableException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SerializableException"/> class.
    /// </summary>
    /// <param name="typeName">Source exception type name.</param>
    /// <param name="message">The message.</param>
    public SerializableException(string typeName, string message)
        : base(message)
    {
        SourceTypeName = typeName;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SerializableException"/> class.
    /// </summary>
    /// <param name="typeName">Source exception type name.</param>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The inner exception.</param>
    public SerializableException(string typeName, string message, Exception innerException)
        : base(message, innerException)
    {
        SourceTypeName = typeName;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SerializableException"/> class.
    /// </summary>
    /// <param name="typeName">Source exception type name.</param>
    /// <param name="message">The message.</param>
    /// <param name="newStackTrace">The new stack trace.</param>
    public SerializableException(string typeName, string message, string newStackTrace)
        : base(message)
    {
        SourceTypeName = typeName;
        stackTrace = newStackTrace;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SerializableException"/> class.
    /// </summary>
    /// <param name="typeName">Source exception type name.</param>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The inner exception.</param>
    /// <param name="newStackTrace">The new stack trace.</param>
    public SerializableException(string typeName, string message, Exception innerException, string newStackTrace)
        : base(message, innerException)
    {
        SourceTypeName = typeName;
        stackTrace = newStackTrace;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SerializableException"/> class.
    /// </summary>
    /// <param name="info">The object that holds the serialized object data.</param>
    /// <param name="context">The contextual information about the source or destination.</param>
    protected SerializableException(SerializationInfo info, StreamingContext context)
        : base(info, context)
    {
        stackTrace = info.GetString("MyStackTrace");
        SourceTypeName = info.GetString("SourceTypeName");
    }

    /// <summary>
    /// Sets the <see cref="T:System.Runtime.Serialization.SerializationInfo" /> with information about the exception.
    /// </summary>
    /// <param name="info">The <see cref="T:System.Runtime.Serialization.SerializationInfo" /> that holds
    /// the serialized object data about the exception being thrown.</param>
    /// <param name="context">The <see cref="T:System.Runtime.Serialization.StreamingContext" /> that contains
    /// contextual information about the source or destination.</param>
    public override void GetObjectData(SerializationInfo info, StreamingContext context)
    {
        base.GetObjectData(info, context);
        info.AddValue("MyStackTrace", stackTrace);
        info.AddValue("SourceTypeName", SourceTypeName);
    }

    private string stackTrace;

    /// <summary>
    /// Gets a string representation of the immediate frames on the call stack.
    /// </summary>
    /// <returns>A string that describes the immediate frames of the call stack.</returns>
    /// <PermissionSet>
    ///   <IPermission class="System.Security.Permissions.FileIOPermission, mscorlib, Version=2.0.3600.0, Culture=neutral, PublicKeyToken=b77a5c561934e089" version="1" PathDiscovery="*AllFiles*"/>
    /// </PermissionSet>
    public override string StackTrace => stackTrace ?? base.StackTrace;

    /// <summary>
    /// Gets the type name of source exception.
    /// </summary>
    public string SourceTypeName { get; private set; }
}
