using System.Threading;
using Cysharp.Threading.Tasks;

namespace Company.ChestGame.Saving
{
    // Where bytes land, keyed by string. Knows nothing about envelopes, codecs or protectors.
    //
    // Cancellation is only observed before the underlying read or write begins, never after it,
    // so a cancelled call has changed nothing. An empty key throws SaveException in every store;
    // file-backed stores also reject a key that is a rooted path or contains "..", a path
    // separator, or a character a file name cannot hold.
    public interface ISaveStore
    {
        // Replaces whatever is stored under key; a null array is stored as empty. Throws
        // SaveException when the bytes cannot be stored.
        UniTask WriteAsync(string key, byte[] bytes, CancellationToken ct);

        // Null when nothing is stored under key. Something stored that cannot be read is thrown.
        UniTask<byte[]> ReadAsync(string key, CancellationToken ct);

        // True exactly when ReadAsync would not return null.
        UniTask<bool> ExistsAsync(string key, CancellationToken ct);

        // Removes whatever is stored under key, so ExistsAsync answers false afterwards. Does
        // nothing when nothing is stored. Throws SaveException when the entry cannot be removed.
        UniTask DeleteAsync(string key, CancellationToken ct);

        // Answer true only if every member above always finishes on the thread that called it,
        // never suspending onto another thread and back. Callers block on the result when this is
        // true, so answering true dishonestly turns a blocking read into a deadlock.
        bool CompletesOnCallingThread { get; }
    }
}
