using System;

namespace CoreRemoting.Tests.Concurrency
{
    /// <summary>
    /// Stable test type to avoid anonymous type issues in multi-threading scenarios.
    /// </summary>
    public class ThreadSafeTestType
    {
        public int ThreadId { get; set; }
        public int OperationId { get; set; }
        public DateTime Timestamp { get; set; }
        public Guid Guid { get; set; }
        public NestedThreadSafeData NestedData { get; set; }
    }

    /// <summary>
    /// Nested test type for ThreadSafeTestType.
    /// </summary>
    public class NestedThreadSafeData
    {
        public int InnerValue { get; set; }
        public string InnerString { get; set; }
    }
}