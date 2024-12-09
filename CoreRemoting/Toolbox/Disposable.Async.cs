using System;
using System.Threading.Tasks;

namespace CoreRemoting.Toolbox
{
    static partial class Disposable
    {
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
