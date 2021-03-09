using System;
using System.Collections.Generic;
using CoreRemoting.Serialization.Bson;

namespace CoreRemoting.RpcMessaging
{
    /// <summary>
    /// Extension methods for messaging.
    /// </summary>
    public static class MessagingExtensionMethods
    {
        /// <summary>
        /// Parses a text message that contains name value pairs.
        /// </summary>
        /// <param name="message">Text message</param>
        /// <param name="entrySeperator">Char used to separate entries</param>
        /// <param name="pairSeperator">Char used to separate pairs</param>
        /// <returns>Dictionary with parsed name value pairs</returns>
        public static Dictionary<string, string> ParseNameValuePairTextMessage(this string message, char entrySeperator = '|', char pairSeperator = ':')
        {
            var entries = message.Split(new[] {entrySeperator}, StringSplitOptions.RemoveEmptyEntries);
            var parsedNameValuePairs = new Dictionary<string, string>();

            foreach (var entry in entries)
            {
                var entryParts = entry.Split(new[] {pairSeperator}, StringSplitOptions.RemoveEmptyEntries);

                if (entryParts.Length != 2)
                    continue;

                parsedNameValuePairs.Add(entryParts[0].Trim().ToLower(), entryParts[1].Trim());
            }

            return parsedNameValuePairs;
        }
        
        /// <summary>
        /// Unwraps parameter values and parameter types from a deserialized MethodCallMessage.
        /// </summary>
        /// <param name="callMessage">MethodCallMessage object</param>
        /// <param name="parameterValues">Out: Unwrapped parameter values</param>
        /// <param name="parameterTypes">Out: Unwrapped parameter types</param>
        public static void UnwrapParametersFromDeserializedMethodCallMessage(
            this MethodCallMessage callMessage, 
            out object[] parameterValues,
            out Type[] parameterTypes)
        {
            parameterTypes = new Type[callMessage.Parameters.Length];
            parameterValues = new object[callMessage.Parameters.Length];

            for (int i = 0; i < callMessage.Parameters.Length; i++)
            {
                var parameter = callMessage.Parameters[i];
                var parameterType = Type.GetType(parameter.ParameterTypeName);
                parameterTypes[i] = parameterType;

                parameterValues[i] =
                    parameter.IsValueNull
                        ? null
                        : parameter.Value is Envelope envelope
                            ? envelope.Value == null 
                                ? null
                                : Convert.ChangeType(envelope.Value, envelope.Type)
                            : parameter.Value;
            }
        }
    }
}