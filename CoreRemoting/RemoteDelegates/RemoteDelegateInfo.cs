using System;
using System.Runtime.Serialization;

namespace CoreRemoting.RemoteDelegates
{
    [DataContract]
    [Serializable]
    public class RemoteDelegateInfo
    {
        [DataMember]
        private Guid _handlerKey;
        [DataMember]
        private string _delegateTypeName;

        public RemoteDelegateInfo(Guid handlerKey, string delegateTypeName)
        {
            _handlerKey = handlerKey;
            _delegateTypeName = delegateTypeName;
        }
        
        public Guid HandlerKey => _handlerKey;

        public string DelegateTypeName => _delegateTypeName;
    }
}