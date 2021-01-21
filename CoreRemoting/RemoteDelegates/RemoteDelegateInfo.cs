using System;
using System.Runtime.Serialization;

namespace CoreRemoting.RemoteDelegates
{
    /// <summary>
    /// Describes a remote delegate.
    /// </summary>
    [DataContract]
    [Serializable]
    public class RemoteDelegateInfo
    {
        [DataMember]
        private Guid _handlerKey;
        
        [DataMember]
        private string _delegateTypeName;

        /// <summary>
        /// Creates a new instance of the RemoteDelegateInfo class.
        /// </summary>
        /// <param name="handlerKey">Unique handler key of the client delegate</param>
        /// <param name="delegateTypeName">Type name of the client delegate</param>
        public RemoteDelegateInfo(Guid handlerKey, string delegateTypeName)
        {
            _handlerKey = handlerKey;
            _delegateTypeName = delegateTypeName;
        }
        
        /// <summary>
        /// Gets the handler key of the client delegate.
        /// </summary>
        public Guid HandlerKey => _handlerKey;

        /// <summary>
        /// Gets the type name of the client delegate.
        /// </summary>
        public string DelegateTypeName => _delegateTypeName;
    }
}