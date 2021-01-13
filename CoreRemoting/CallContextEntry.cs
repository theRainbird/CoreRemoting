using System;

namespace CoreRemoting
{
    [Serializable]
    public class CallContextEntry
    {
        public string Name { get; set; }
        
        public object Value { get; set; }
    }
}