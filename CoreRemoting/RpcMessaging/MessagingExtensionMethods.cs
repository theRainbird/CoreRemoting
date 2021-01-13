using System;
using System.Collections.Generic;

namespace CoreRemoting.RpcMessaging
{
    public static class MessagingExtensionMethods
    {
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