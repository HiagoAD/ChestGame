using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Company.ChestGame.Saving
{
    // Wraps another ISaveStore and moves its call onto the thread pool and back, so the blocking
    // disk IO FileStore and AtomicFileStore do - and AtomicFileStore's own FileStream.Flush(true) in
    // particular - does not run on the thread Unity needs for everything else that frame. Skipped
    // entirely when the wrapped store is IMainThreadOnlyStore: PlayerPrefsStore can only ever be
    // called from whatever thread Unity itself calls it on, so wrapping it here would either do
    // nothing safely useful or break it, and the marker is what tells this type which one applies
    // without hard-coding PlayerPrefsStore by name. See docs/saving.md, "The thread hop".
    //
    // Encode and protect never reach this type. SaveService calls ISaveCodec.Encode and
    // IPayloadProtector.Protect on whatever thread called SaveAsync, before _store.WriteAsync is
    // ever reached - unchanged by this class existing at all, because this class only ever wraps
    // the store SaveService already holds. Wrapping the store rather than SaveService itself is
    // what keeps that ordering true regardless of which store a profile picks: only a byte[] neither
    // gameplay nor anything else still holds a reference to ever crosses the hop this type performs,
    // never the caller-owned, still-mutable state object SaveAsync was actually given. See
    // docs/saving.md, "Where the hop sits, and why not SaveService" for the reasoning this class is
    // the answer to.
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

        // False unless the wrapped store is IMainThreadOnlyStore, in which case every member above
        // calls straight through and never hops at all. This is the fact SaveScheduler<T>'s
        // CanFlushBlocking exists to read - see docs/saving.md, "FlushBlocking, and why it cannot
        // deadlock" - so a scheduler can know ahead of time, at composition time, whether
        // FlushBlocking will ever be able to succeed on it, rather than discovering that only when a
        // write happens to be in flight the moment it is called.
        public bool CompletesOnCallingThread => _mainThreadOnly;

        // CancellationToken.None here on purpose, not ct: every method above already checked ct
        // before ever calling this, and the closure it hands in checks it again as the first thing
        // the wrapped store does on the thread-pool side, exactly as it always has. RunOnThreadPool
        // would otherwise re-check its own cancellationToken argument a third time on the way back
        // across UniTask.Yield, after the write already finished - which would report a save as
        // canceled that had, in fact, already reached disk. Passing None here is what keeps a
        // cancellation only ever observed before a write starts, never lied about after one already
        // finished.
        private static UniTask HopAsync(Func<UniTask> operation) =>
            UniTask.RunOnThreadPool(operation, cancellationToken: CancellationToken.None);

        private static UniTask<T> HopAsync<T>(Func<UniTask<T>> operation) =>
            UniTask.RunOnThreadPool(operation, cancellationToken: CancellationToken.None);
    }
}
