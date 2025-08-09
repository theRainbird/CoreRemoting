﻿using System;
using System.Runtime.Serialization;
using System.Threading;

namespace CoreRemoting.Tests.Tools
{
    /// <summary>
    /// Simulates a heavy object.
    /// </summary>
    public class HeavyweightObjectSimulator(bool init = false)
    {
        private static int lastValue = 0;

        public int Counter { get; set; } = init ? Interlocked.Increment(ref lastValue) : 0;

        public int SerializationDelay { get; set; }

        [OnSerializing]
        internal void OnSerializingMethod(StreamingContext context)
        {
            if (SerializationDelay > 0)
            {
                Thread.Sleep(Math.Min(SerializationDelay, 1000));
            }
        }
    }
}
