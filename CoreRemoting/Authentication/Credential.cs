using System;

namespace CoreRemoting.Authentication
{
    /// <summary>
    /// Describes an authentication credential.
    /// </summary>
    [Serializable]
    public class Credential
    {
        /// <summary>
        /// Gets or sets the name of the credential (e.G. "password").
        /// </summary>
        public string Name { get; set; }
        
        /// <summary>
        /// Get or sets the value of the crdential (e.G. "secret").
        /// </summary>
        public string Value { get; set; }
    }
}