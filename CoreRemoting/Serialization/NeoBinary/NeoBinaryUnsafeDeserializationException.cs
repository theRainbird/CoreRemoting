using System;

namespace CoreRemoting.Serialization.NeoBinary;

/// <summary>
/// Exception thrown when an unsafe deserialization operation is detected in NeoBinary serializer.
/// </summary>
public class NeoBinaryUnsafeDeserializationException : Exception
{
	/// <summary>
	/// Creates a new instance of the NeoBinaryUnsafeDeserializationException class.
	/// </summary>
	public NeoBinaryUnsafeDeserializationException()
		: base("Unsafe deserialization operation detected.")
	{
	}

	/// <summary>
	/// Creates a new instance of the NeoBinaryUnsafeDeserializationException class with a specified error message.
	/// </summary>
	/// <param name="message">The message that describes the error</param>
	public NeoBinaryUnsafeDeserializationException(string message)
		: base(message)
	{
	}

	/// <summary>
	/// Creates a new instance of the NeoBinaryUnsafeDeserializationException class with a specified error message and a reference to the inner exception that is the cause of this exception.
	/// </summary>
	/// <param name="message">The message that describes the error</param>
	/// <param name="innerException">The exception that is the cause of the current exception</param>
	public NeoBinaryUnsafeDeserializationException(string message, Exception innerException)
		: base(message, innerException)
	{
	}

	/// <summary>
	/// Creates a new instance of the NeoBinaryUnsafeDeserializationException class with serialized data.
	/// </summary>
	/// <param name="info">The SerializationInfo that holds the serialized object data about the exception being thrown</param>
	/// <param name="context">The StreamingContext that contains contextual information about the source or destination</param>
	protected NeoBinaryUnsafeDeserializationException(
		System.Runtime.Serialization.SerializationInfo info,
		System.Runtime.Serialization.StreamingContext context)
		: base(info, context)
	{
	}

	/// <summary>
	/// Gets or sets the type name that caused the unsafe deserialization attempt.
	/// </summary>
	public string TypeName { get; set; }

	/// <summary>
	/// Gets or sets the assembly name that caused the unsafe deserialization attempt.
	/// </summary>
	public string AssemblyName { get; set; }

	/// <summary>
	/// Gets or sets the reason why the deserialization was considered unsafe.
	/// </summary>
	public string UnsafeReason { get; set; }

	/// <summary>
	/// Creates a new instance of the NeoBinaryUnsafeDeserializationException class with detailed information about the unsafe operation.
	/// </summary>
	/// <param name="typeName">The type name that caused the unsafe deserialization</param>
	/// <param name="assemblyName">The assembly name that caused the unsafe deserialization</param>
	/// <param name="reason">The reason why the deserialization is unsafe</param>
	/// <returns>A new NeoBinaryUnsafeDeserializationException instance</returns>
	public static NeoBinaryUnsafeDeserializationException Create(string typeName, string assemblyName, string reason)
	{
		var message = $"Unsafe deserialization of type '{typeName}' from assembly '{assemblyName}' detected: {reason}";

		return new NeoBinaryUnsafeDeserializationException(message)
		{
			TypeName = typeName,
			AssemblyName = assemblyName,
			UnsafeReason = reason
		};
	}

	/// <summary>
	/// Creates a new instance of the NeoBinaryUnsafeDeserializationException class for a blocked type.
	/// </summary>
	/// <param name="typeName">The blocked type name</param>
	/// <param name="assemblyName">The assembly name of the blocked type</param>
	/// <returns>A new NeoBinaryUnsafeDeserializationException instance</returns>
	public static NeoBinaryUnsafeDeserializationException ForBlockedType(string typeName, string assemblyName)
	{
		return Create(typeName, assemblyName, "Type is explicitly blocked for security reasons");
	}

	/// <summary>
	/// Creates a new instance of the NeoBinaryUnsafeDeserializationException class for a blocked namespace.
	/// </summary>
	/// <param name="typeName">The type name</param>
	/// <param name="namespace">The blocked namespace</param>
	/// <returns>A new NeoBinaryUnsafeDeserializationException instance</returns>
	public static NeoBinaryUnsafeDeserializationException ForBlockedNamespace(string typeName, string @namespace)
	{
		return Create(typeName, string.Empty, $"Type belongs to blocked namespace '{@namespace}'");
	}

	/// <summary>
	/// Creates a new instance of the NeoBinaryUnsafeDeserializationException class for an unknown type.
	/// </summary>
	/// <param name="typeName">The unknown type name</param>
	/// <param name="assemblyName">The assembly name</param>
	/// <returns>A new NeoBinaryUnsafeDeserializationException instance</returns>
	public static NeoBinaryUnsafeDeserializationException ForUnknownType(string typeName, string assemblyName)
	{
		return Create(typeName, assemblyName, "Unknown type is not allowed in strict security mode");
	}

	/// <summary>
	/// Creates a new instance of the NeoBinaryUnsafeDeserializationException class for a delegate type.
	/// </summary>
	/// <param name="typeName">The delegate type name</param>
	/// <param name="assemblyName">The assembly name</param>
	/// <returns>A new NeoBinaryUnsafeDeserializationException instance</returns>
	public static NeoBinaryUnsafeDeserializationException ForDelegateType(string typeName, string assemblyName)
	{
		return Create(typeName, assemblyName, "Delegate deserialization is not allowed for security reasons");
	}

	/// <summary>
	/// Creates a new instance of the NeoBinaryUnsafeDeserializationException class for a dynamic assembly type.
	/// </summary>
	/// <param name="typeName">The type name</param>
	/// <param name="assemblyName">The dynamic assembly name</param>
	/// <returns>A new NeoBinaryUnsafeDeserializationException instance</returns>
	public static NeoBinaryUnsafeDeserializationException ForDynamicAssembly(string typeName, string assemblyName)
	{
		return Create(typeName, assemblyName, "Types from dynamic assemblies are not allowed for security reasons");
	}

	/// <summary>
	/// Creates a new instance of the NeoBinaryUnsafeDeserializationException class for a dangerous type combination.
	/// </summary>
	/// <param name="typeName">The type name</param>
	/// <param name="assemblyName">The assembly name</param>
	/// <returns>A new NeoBinaryUnsafeDeserializationException instance</returns>
	public static NeoBinaryUnsafeDeserializationException ForDangerousTypeCombination(string typeName,
		string assemblyName)
	{
		return Create(typeName, assemblyName, "Type has potentially dangerous attribute or interface combination");
	}

	/// <summary>
	/// Creates a new instance of the NeoBinaryUnsafeDeserializationException class for a dangerous interface implementation.
	/// </summary>
	/// <param name="typeName">The type name</param>
	/// <param name="assemblyName">The assembly name</param>
	/// <param name="interfaceName">The dangerous interface name</param>
	/// <returns>A new NeoBinaryUnsafeDeserializationException instance</returns>
	public static NeoBinaryUnsafeDeserializationException ForDangerousInterface(string typeName, string assemblyName,
		string interfaceName)
	{
		return Create(typeName, assemblyName, $"Type implements dangerous interface '{interfaceName}'");
	}
}