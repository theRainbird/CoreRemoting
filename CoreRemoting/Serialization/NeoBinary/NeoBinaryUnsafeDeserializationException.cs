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
#pragma warning disable SYSLIB0051
		: base(info, context)
#pragma warning restore SYSLIB0051
	{
	}
}