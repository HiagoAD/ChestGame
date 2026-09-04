using System.Threading;
using Cysharp.Threading.Tasks;

namespace Company.ChestGame.Saving
{
    // Where bytes land, keyed by string. Knows nothing about envelopes, codecs or protectors.
    public interface ISaveStore
    {
        UniTask WriteAsync(string key, byte[] bytes, CancellationToken ct);

        // Null when nothing is stored under key. Something stored that cannot be read is thrown.
        UniTask<byte[]> ReadAsync(string key, CancellationToken ct);

        UniTask<bool> ExistsAsync(string key, CancellationToken ct);

        UniTask DeleteAsync(string key, CancellationToken ct);

        // True when every member above always finishes on the thread that called it - never
        // suspending to a worker thread and back. Every store this assembly ships answers true;
        // ThreadHoppingStore is the one exception, answering false for whatever it wraps unless
        // that inner store is IMainThreadOnlyStore, in which case it never actually hops either.
        // This is what lets SaveScheduler<T>.CanFlushBlocking answer honestly without knowing any
        // concrete store by name - the same reasoning IMainThreadOnlyStore already follows for a
        // different question. See docs/saving.md, "FlushBlocking, and why it cannot deadlock".
        bool CompletesOnCallingThread { get; }
    }
}
