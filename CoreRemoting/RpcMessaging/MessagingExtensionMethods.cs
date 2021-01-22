using System;
using System.Collections.Generic;

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
        /// <param name="entrySeperator">Char used to seperate entries</param>
        /// <param name="pairSeperator">Char used to seperate pairs</param>
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
    }
}