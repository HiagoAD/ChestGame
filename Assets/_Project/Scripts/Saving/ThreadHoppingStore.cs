using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Company.ChestGame.Saving
{
    /// <summary>
    /// Moves a wrapped store's call onto the thread pool and back. Skipped entirely for an inner
    /// store that implements <see cref="IMainThreadOnlyStore"/>.
    /// </summary>
    /// <remarks>
    /// See docs/saving.md, "The thread hop, and why it is not inside SaveService".
    /// See docs/saving.md, "Where the hop sits, and why not SaveService - the torn-save invariant".
    /// </remarks>
    public class ThreadHoppingStore : ISaveStore
    {
        private readonly ISaveStore _inner;
        private readonly bool _mainThreadOnly;

        public ThreadHoppingStore(ISaveStore inner)
        {
            if (inner == null) throw SaveException.NoStore();

            _inner = inner;
            _mainThreadOnly = inner is IMainThreadOnlyStore;
        }

        public UniTask WriteAsync(string key, byte[] bytes, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            return _mainThreadOnly ? _inner.WriteAsync(key, bytes, ct) : HopAsync(() => _inner.WriteAsync(key, bytes, ct));
        }

        public UniTask<byte[]> ReadAsync(string key, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            return _mainThreadOnly ? _inner.ReadAsync(key, ct) : HopAsync(() => _inner.ReadAsync(key, ct));
        }

        public UniTask<bool> ExistsAsync(string key, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            return _mainThreadOnly ? _inner.ExistsAsync(key, ct) : HopAsync(() => _inner.ExistsAsync(key, ct));
        }

        public UniTask DeleteAsync(string key, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            return _mainThreadOnly ? _inner.DeleteAsync(key, ct) : HopAsync(() => _inner.DeleteAsync(key, ct));
        }

        /// <summary>
        /// True only when the wrapped store was never going to leave the calling thread anyway - the
        /// one case where this type does no hopping at all. Callers may block on a result only when
        /// this answers true.
        /// </summary>
        /// <remarks>
        /// See docs/saving.md, "The thread hop, and why it is not inside SaveService".
        /// </remarks>
        public bool CompletesOnCallingThread => _mainThreadOnly;

        /// <remarks>
        /// See docs/saving.md, "The thread hop, and why it is not inside SaveService".
        /// </remarks>
        private static UniTask HopAsync(Func<UniTask> operation) =>
            UniTask.RunOnThreadPool(operation, cancellationToken: CancellationToken.None);

        private static UniTask<T> HopAsync<T>(Func<UniTask<T>> operation) =>
            UniTask.RunOnThreadPool(operation, cancellationToken: CancellationToken.None);
    }
}
