using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Company.ChestGame.Saving
{
    // Moves a wrapped store's call onto the thread pool and back, so blocking disk IO does not run
    // on the thread Unity needs for the rest of the frame. Skipped entirely for an
    // IMainThreadOnlyStore - decided through the marker interface, never by checking for a concrete
    // store type.
    //
    // Wrapping the store, not the whole save pipeline, is what keeps encode and protect on the
    // calling thread: only a byte[] nothing else still references crosses the hop, never the
    // caller-owned, still-mutable state object a save call was given. See docs/saving.md for the
    // thread hop's full reasoning.
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

        // True only when the wrapped store was never going to leave the calling thread anyway - the
        // one case where this type does no hopping at all. Callers may block on a result only when
        // this answers true.
        public bool CompletesOnCallingThread => _mainThreadOnly;

        // CancellationToken.None on purpose, not ct: every method above already checked ct, and
        // the wrapped store checks it again on the thread-pool side. RunOnThreadPool would
        // otherwise re-check a third time on the way back across UniTask.Yield, after the write
        // finished - reporting a save as cancelled that had in fact already reached disk.
        private static UniTask HopAsync(Func<UniTask> operation) =>
            UniTask.RunOnThreadPool(operation, cancellationToken: CancellationToken.None);

        private static UniTask<T> HopAsync<T>(Func<UniTask<T>> operation) =>
            UniTask.RunOnThreadPool(operation, cancellationToken: CancellationToken.None);
    }
}
