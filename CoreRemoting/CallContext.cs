using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace CoreRemoting
{
    /// <summary>
    /// Provides a way to set contextual data that flows with the call and 
    /// async context of a invocation.
    /// </summary>
    public static class CallContext
    {
        private static readonly ConcurrentDictionary<string, AsyncLocal<object>> State = 
            new ConcurrentDictionary<string, AsyncLocal<object>>();
        
        /// <summary>
        /// Stores a given object and associates it with the specified name.
        /// </summary>
        /// <param name="name">The name with which to associate the new item in the call context.</param>
        /// <param name="data">The object to store in the call context.</param>
        public static void SetData(string name, object data) =>
            State.GetOrAdd(name, _ => new AsyncLocal<object>()).Value = data;

        /// <summary>
        /// Retrieves an object with the specified name from the <see cref="CallContext"/>.
        /// </summary>
        /// <param name="name">The name of the item in the call context.</param>
        /// <returns>The object in the call context associated with the specified name, or <see langword="null"/> if not found.</returns>
        public static object GetData(string name) =>
            State.TryGetValue(name, out AsyncLocal<object> data) ? data.Value : null;

        /// <summary>
        /// Gets a serializable snapshot of the current call context.
        /// </summary>
        /// <returns>Array of call context entries</returns>
        public static CallContextEntry[] GetSnapshot()
        {
            var stateSnaphsot = State.ToArray();
            var result = new CallContextEntry[stateSnaphsot.Length];

            for(int i = 0; i< stateSnaphsot.Length; i++)
            {
                var entry = stateSnaphsot[i];
                
                result[i] =
                    new CallContextEntry()
                    {
                        Name = entry.Key,
                        Value = entry.Value.Value
                    };
            }

            return result;
        }

        /// <summary>
        /// Restore the call context from a snapshot.
        /// </summary>
        /// <param name="entries">Call context entries</param>
        public static void RestoreFromSnapshot(IEnumerable<CallContextEntry> entries)
        {
            if (entries == null)
            {
                foreach (var entry in State)
                {
                    SetData(entry.Key, null);
                }
                return;
            }
            
            foreach (var entry in entries)
            {
                CallContext.SetData(entry.Name, entry.Value);
            }
        }
    }
}