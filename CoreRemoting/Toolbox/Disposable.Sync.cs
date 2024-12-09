using System;

namespace CoreRemoting.Toolbox
{
    /// <summary>
    /// Helper class to create disposable primitives.
    /// </summary>
    public static partial class Disposable
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

        private class ParamsDisposable(params IDisposable[] disposables) : IDisposable
        {
            void IDisposable.Dispose()
            {
                foreach (var disposable in disposables ?? [])
                    disposable?.Dispose();
            }
        }

        /// <summary>
        /// Creates a disposable object.
        /// </summary>
        /// <param name="disposables">Disposable items to dispose on disposal.</param>
        public static IDisposable Create(params IDisposable[] disposables) =>
            new ParamsDisposable(disposables);
    }
}
