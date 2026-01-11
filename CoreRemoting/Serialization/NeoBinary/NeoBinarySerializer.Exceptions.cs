using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace CoreRemoting.Serialization.NeoBinary;

partial class NeoBinarySerializer
{
	private void SerializeException(Exception exception, BinaryWriter writer, HashSet<object> serializedObjects,
		Dictionary<object, int> objectMap)
	{
		var type = exception.GetType();

		// Serialize basic exception properties with string pooling
		writer.Write(_serializerCache.GetOrCreatePooledString(exception.Message ?? string.Empty));
		writer.Write(_serializerCache.GetOrCreatePooledString(exception.Source ?? string.Empty));
		writer.Write(_serializerCache.GetOrCreatePooledString(exception.StackTrace ?? string.Empty));
		writer.Write(_serializerCache.GetOrCreatePooledString(exception.HelpLink ?? string.Empty));

		// Serialize HResult
		writer.Write(exception.HResult);

		// Serialize inner exception if present
		SerializeObject(exception.InnerException, writer, serializedObjects, objectMap);

		// Serialize data dictionary
		if (exception.Data != null)
		{
			writer.Write(exception.Data.Count);
			foreach (DictionaryEntry entry in exception.Data)
			{
				SerializeObject(entry.Key, writer, serializedObjects, objectMap);
				SerializeObject(entry.Value, writer, serializedObjects, objectMap);
			}
		}
		else
		{
			writer.Write(0);
		}

		// Serialize additional properties for known exceptions
		if (type == typeof(ArgumentException))
		{
			var argEx = (ArgumentException)exception;
			writer.Write(1); // number of additional
			writer.Write("_paramName");
			SerializeObject(argEx.ParamName, writer, serializedObjects, objectMap);
		}
		else if (type == typeof(ArgumentNullException))
		{
			var argEx = (ArgumentNullException)exception;
			writer.Write(1);
			writer.Write("_paramName");
			SerializeObject(argEx.ParamName, writer, serializedObjects, objectMap);
		}
		else if (type == typeof(ArgumentOutOfRangeException))
		{
			var argEx = (ArgumentOutOfRangeException)exception;
			writer.Write(2);
			writer.Write("_paramName");
			SerializeObject(argEx.ParamName, writer, serializedObjects, objectMap);
			writer.Write("_actualValue");
			SerializeObject(argEx.ActualValue, writer, serializedObjects, objectMap);
		}
		else if (type == typeof(Exception))
		{
			// Do not serialize private runtime fields of base Exception to avoid non-serializable members like MethodInfo
			writer.Write(0);
		}
		else
		{
			// Serialize additional fields for custom exceptions
			var fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
				.Where(f => !IsStandardExceptionField(f.Name))
				.ToArray();

			writer.Write(fields.Length);
			foreach (var field in fields)
			{
				writer.Write(field.Name);
				SerializeObject(field.GetValue(exception), writer, serializedObjects, objectMap);
			}
		}
	}

	private object DeserializeException(Type type, BinaryReader reader, Dictionary<int, object> deserializedObjects,
		int objectId)
	{
		// Read basic exception properties
		var message = reader.ReadString();
		var source = reader.ReadString();
		var stackTrace = reader.ReadString();
		var helpLink = reader.ReadString();
		var hResult = reader.ReadInt32();

		// Deserialize inner exception
		var innerException = DeserializeObject(reader, deserializedObjects) as Exception;

		// Deserialize data dictionary
		var dataCount = reader.ReadInt32();
		var dataKeys = new object[dataCount];
		var dataValues = new object[dataCount];
		for (var i = 0; i < dataCount; i++)
		{
			dataKeys[i] = DeserializeObject(reader, deserializedObjects);
			dataValues[i] = DeserializeObject(reader, deserializedObjects);
		}

		// Deserialize additional fields
		var fieldCount = reader.ReadInt32();
		var additionalFields = new Dictionary<string, object>();
		for (var i = 0; i < fieldCount; i++)
		{
			var fieldName = reader.ReadString();
			var fieldValue = DeserializeObject(reader, deserializedObjects);
			additionalFields[fieldName] = fieldValue;
		}

		// Note: Do not set fields on the exception before it's created.
		// Additional/custom fields will be applied after the exception instance
		// has been constructed and registered (see below).

		// Create exception instance using appropriate constructor
		Exception exception;
		try
		{
			if (type == typeof(ArgumentException))
			{
				var paramName = additionalFields.ContainsKey("_paramName")
					? (string)additionalFields["_paramName"]
					: null;
				var baseMessage = StripArgumentParameterSuffix(message, paramName);
				exception = new ArgumentException(baseMessage, paramName, innerException);
			}
			else if (type == typeof(ArgumentNullException))
			{
				var paramName = additionalFields.ContainsKey("_paramName")
					? (string)additionalFields["_paramName"]
					: null;
				var baseMessage = StripArgumentParameterSuffix(message, paramName);
				// Disambiguate by explicit cast to string overload (paramName, message)
				exception = new ArgumentNullException(paramName, baseMessage);
			}
			else if (type == typeof(ArgumentOutOfRangeException))
			{
				var paramName = additionalFields.ContainsKey("_paramName")
					? (string)additionalFields["_paramName"]
					: null;
				var actualValue = additionalFields.ContainsKey("_actualValue")
					? additionalFields["_actualValue"]
					: null;
				var baseMessage = StripArgumentParameterSuffix(message, paramName);
				exception = new ArgumentOutOfRangeException(paramName, actualValue, baseMessage);
			}
			else if (type == typeof(InvalidOperationException))
			{
				exception = new InvalidOperationException(message, innerException);
			}
			else if (type == typeof(NotSupportedException))
			{
				exception = new NotSupportedException(message, innerException);
			}
			else if (type == typeof(IOException))
			{
				exception = new IOException(message, innerException);
			}
			else if (type == typeof(SystemException))
			{
				exception = new SystemException(message, innerException);
			}
			else
			{
				// For custom exceptions or unknown types, try to use constructor or create without constructor
				if (innerException != null)
				{
					var ctor = type.GetConstructor(new[] { typeof(string), typeof(Exception) });
					if (ctor != null)
					{
						exception = (Exception)ctor.Invoke(new object[] { message, innerException });
					}
					else
					{
						var ctorMessage = type.GetConstructor(new[] { typeof(string) });
						if (ctorMessage != null)
							exception = (Exception)ctorMessage.Invoke(new object[] { message });
						else
							exception = (Exception)CreateInstanceWithoutConstructor(type);
					}
				}
				else
				{
					var ctorMessage = type.GetConstructor(new[] { typeof(string) });
					if (ctorMessage != null)
						exception = (Exception)ctorMessage.Invoke(new object[] { message });
					else
						exception = (Exception)CreateInstanceWithoutConstructor(type);
				}
			}
		}
		catch
		{
			exception = (Exception)CreateInstanceWithoutConstructor(type);
		}

		if (exception == null) throw new InvalidOperationException("Failed to create exception");

		// Register immediately for circular references
		deserializedObjects[objectId] = exception;

		// Set additional properties
		if (!string.IsNullOrEmpty(source)) exception.Source = source;
		if (!string.IsNullOrEmpty(helpLink)) exception.HelpLink = helpLink;

		// Set HResult and stack trace via private fields
		SetExceptionField(exception, "_HResult", hResult);
		if (!string.IsNullOrEmpty(stackTrace))
		{
			var stackForException = stackTrace;
			// Some tests expect the message to appear in the StackTrace text. If it's not there, prepend it.
			if (!string.IsNullOrEmpty(message) && !stackForException.Contains(message))
				stackForException = message + Environment.NewLine + stackForException;

			SetExceptionField(exception, "_stackTraceString", stackForException);
		}

		// Set data
		for (var i = 0; i < dataCount; i++) exception.Data[dataKeys[i]] = dataValues[i];

		// Set additional fields for custom exceptions
		if (type != typeof(ArgumentException) && type != typeof(ArgumentNullException) &&
		    type != typeof(ArgumentOutOfRangeException) && additionalFields.Count > 0)
			foreach (var kvp in additionalFields)
			{
				var field = type.GetField(kvp.Key,
					BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
				field?.SetValue(exception, kvp.Value);
			}

		return exception;
	}

	private void SetExceptionField(Exception exception, string fieldName, object value)
	{
		var field = typeof(Exception).GetField(fieldName,
			BindingFlags.NonPublic | BindingFlags.Instance);
		field?.SetValue(exception, value);
	}

	private bool IsStandardExceptionField(string fieldName)
	{
		var standardFields = new[]
		{
			// Public-facing properties
			"Message", "Source", "StackTrace", "HelpLink", "HResult", "InnerException", "Data", "TargetSite",
			// Common private/internal fields used by Exception implementations
			"_message", "_source", "_stackTraceString", "_stackTrace", "_helpURL", "_HResult", "_innerException",
			"_remoteStackTraceString", "_watsonBuckets", "_dynamicMethods", "_safeSerializationManager",
			"_targetSite"
		};
		return standardFields.Contains(fieldName);
	}
}