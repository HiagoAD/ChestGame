using System.Threading;
using Cysharp.Threading.Tasks;

namespace Company.ChestGame.Saving
{
    /// <summary>
    /// Where bytes land, keyed by string. Knows nothing about envelopes, codecs or protectors.
    /// </summary>
    /// <remarks>
    /// Cancellation is only observed before the underlying read or write begins, never after it,
    /// so a cancelled call has changed nothing. An empty key throws <see cref="SaveException"/> in
    /// every store; file-backed stores also reject a key that is a rooted path or contains "..", a
    /// path separator, or a character a file name cannot hold.
    /// </remarks>
    public interface ISaveStore
    {
        /// <summary>
        /// Replaces whatever is stored under <paramref name="key"/>; a null array is stored as
        /// empty.
        /// </summary>
        /// <exception cref="SaveException">When the bytes cannot be stored.</exception>
        UniTask WriteAsync(string key, byte[] bytes, CancellationToken ct);

        /// <summary>Null when nothing is stored under <paramref name="key"/>.</summary>
        /// <exception cref="SaveException">When something is stored that cannot be read.</exception>
        UniTask<byte[]> ReadAsync(string key, CancellationToken ct);

        /// <summary>True exactly when <see cref="ReadAsync"/> would not return null.</summary>
        UniTask<bool> ExistsAsync(string key, CancellationToken ct);

        /// <summary>
        /// Removes whatever is stored under <paramref name="key"/>, so <see cref="ExistsAsync"/>
        /// answers false afterwards. Does nothing when nothing is stored.
        /// </summary>
        /// <exception cref="SaveException">When the entry cannot be removed.</exception>
        UniTask DeleteAsync(string key, CancellationToken ct);

        /// <summary>
        /// Answer true only if every member above always finishes on the thread that called it,
        /// never suspending onto another thread and back. Callers block on the result when this is
        /// true, so answering true dishonestly turns a blocking read into a deadlock.
        /// </summary>
        bool CompletesOnCallingThread { get; }
    }
}
