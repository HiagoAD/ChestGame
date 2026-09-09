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

        // Answer true only if every member above always finishes on the thread that called it,
        // never suspending onto another thread and back. Callers block on the result when this is
        // true, so answering true dishonestly turns a blocking read into a deadlock.
        bool CompletesOnCallingThread { get; }
    }
}
