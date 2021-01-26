using System;

namespace CoreRemoting
{
    /// <summary>
    /// Describes a single call context entry.
    /// </summary>
    [Serializable]
    public class CallContextEntry
    {
        /// <summary>
        /// Gets or sets the name of the call context entry. 
        /// </summary>
        public string Name { get; set; }
        
        /// <summary>
        /// Gets or sets the value of the call context entry.
        /// </summary>
        public object Value { get; set; }
    }
}