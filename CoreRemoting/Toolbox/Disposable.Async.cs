using System;
using System.Threading.Tasks;

namespace CoreRemoting.Toolbox;

static partial class Disposable
{
    private class AsyncDisposable(
        Func<ValueTask> disposeAsync,
        Func<Task> disposeTaskAsync) : IAsyncDisposable
    {
        async ValueTask IAsyncDisposable.DisposeAsync()
        {
            if (disposeAsync != null)
                await disposeAsync()
                    .ConfigureAwait(false);

            if (disposeTaskAsync != null)
                await disposeTaskAsync()
                    .ConfigureAwait(false);
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

    private class ParamsDisposableAsync(params object[] disposables) : IAsyncDisposable
    {
        async ValueTask IAsyncDisposable.DisposeAsync()
        {
            foreach (var disposable in disposables ?? [])
                if (disposable is IAsyncDisposable asyncDisposable)
                    await asyncDisposable.DisposeAsync()
                        .ConfigureAwait(false);
                else if (disposable is IDisposable syncDisposable)
                    syncDisposable.Dispose();
        }
    }

    /// <summary>
    /// Creates an asynchronous disposable object.
    /// </summary>
    /// <param name="disposables">Disposable items to dispose on disposal, both sync and async.</param>
    public static IAsyncDisposable Create(params object[] disposables) =>
        new ParamsDisposableAsync(disposables);
}
