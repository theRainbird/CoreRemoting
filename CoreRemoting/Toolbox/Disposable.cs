using System;
using System.Threading.Tasks;

namespace CoreRemoting.Toolbox
{
    /// <summary>
    /// Helper class to create disposable primitives.
    /// </summary>
    public static class Disposable
    {
        private class SyncDisposable(Action disposeAction) : IDisposable
        {
            void IDisposable.Dispose() =>
                disposeAction?.Invoke();
        }

        /// <summary>
        /// Creates a disposable object.
        /// </summary>
        /// <param name="disposeAction">An action to invoke on disposal.</param>
        public static IDisposable Create(Action disposeAction) =>
            new SyncDisposable(disposeAction);

        private class AsyncDisposable(
            Func<ValueTask> disposeAsync,
            Func<Task> disposeTaskAsync) : IAsyncDisposable
        {
            async ValueTask IAsyncDisposable.DisposeAsync()
            {
                if (disposeAsync != null)
                    await disposeAsync();

                if (disposeTaskAsync != null)
                    await disposeTaskAsync();
            }
        }

        /// <summary>
        /// Creates an asynchronous disposable object.
        /// </summary>
        /// <param name="disposeAsync">An action to invoke on disposal.</param>
        public static IAsyncDisposable Create(Func<ValueTask> disposeAsync) =>
            new AsyncDisposable(disposeAsync, null);

        /// <summary>
        /// Creates an asynchronous disposable object.
        /// </summary>
        /// <param name="disposeAsync">An action to invoke on disposal.</param>
        public static IAsyncDisposable Create(Func<Task> disposeAsync) =>
            new AsyncDisposable(null, disposeAsync);
    }
}
