using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using CoreRemoting.Toolbox;
using Xunit;

namespace CoreRemoting.Tests.Tools
{
    /// <summary>
    /// Synchronization context for validating the ConfigureAwait usage across the library.
    /// The idea is that if ConfigureAwait(false) is missing somewhere, then the continuation
    /// is posted to the current synchronization context and can be detected automatically.
    /// 
    /// Post or Send methods are called on the worker threads and the exceptions will be lost.
    /// But Dispose is called on the main thread, so it can throw, and the exception will be
    /// detected and reported by the unit test runner.
    /// 
    /// References:
    /// 1. https://btburnett.com/2016/04/testing-an-sdk-for-asyncawait-synchronizationcontext-deadlocks.html
    /// 2. https://blog.stephencleary.com/2012/07/dont-block-on-async-code.html
    /// </summary>
    public class ValidationSyncContext : SynchronizationContext, IDisposable
    {
        private ConcurrentDictionary<int, (string method, StackTrace trace)> errors = new();

        public void Dispose()
        {
            if (errors.Any())
            {
                var message = "Post or Send methods were called " + errors.Count + " times.";
                Console.WriteLine(message);
                foreach (var pair in errors)
                {
                    Console.WriteLine("====================");
                    Console.WriteLine($"{pair.Value.method}");
                    Console.WriteLine("====================");
                    Console.WriteLine($"{pair.Value.trace}");
                }

                Assert.Fail(message);
            }
        }

        public override void Post(SendOrPostCallback d, object state)
        {
            errors.GetOrAdd(errors.Count, c => (nameof(Post), new StackTrace()));

            base.Post(d, state);
        }

        public override void Send(SendOrPostCallback d, object state)
        {
            errors.GetOrAdd(errors.Count, c => (nameof(Send), new StackTrace()));

            base.Send(d, state);
        }

        public static IDisposable UseSyncContext(SynchronizationContext ctx)
        {
            var oldSyncContext = Current;
            SetSynchronizationContext(ctx);
            
            return Disposable.Create(() =>
            {
                SetSynchronizationContext(oldSyncContext);
                if (ctx is IDisposable disposable)
                    disposable.Dispose();
            });
        }

        public static IDisposable Install() =>
            UseSyncContext(new ValidationSyncContext());
    }
}
