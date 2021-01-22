using System;
using System.Runtime.Serialization;

namespace CoreRemoting.RpcMessaging
{
    /// <summary>
    /// Describes a good bye message, which must be send from client to server in order to end a session.
    /// </summary>
    [DataContract]
    [Serializable]
    public class GoodbyeMessage
    {
        /// <summary>
        /// Gets or sets the session ID of the session that should be ended.
        /// </summary>
        [DataMember]
        public Guid SessionId { get; set; }
    }
}