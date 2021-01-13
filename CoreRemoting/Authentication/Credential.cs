using System;

namespace CoreRemoting.Authentication
{
    [Serializable]
    public class Credential
    {
        public string Name { get; set; }
        
        public string Value { get; set; }
    }
}