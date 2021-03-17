using System;

namespace TaskDemoAppNetRemoting.Shared
{
    [Serializable]
    public class Todo
    {
        public Guid Id { get; set; }
        
        public string Description { get; set; }

        public bool Completed { get; set; }
    }
}