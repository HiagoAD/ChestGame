using System.Collections.Generic;
using System.IO;
using System.Threading;
using Company.ChestGame.Saving;
using Cysharp.Threading.Tasks;

namespace Company.ChestGame.Tests.EditMode
{
    /// <summary>
    /// An in-memory <see cref="ISaveStore"/>, so <c>SaveService</c>'s own logic (the
    /// first-run/corrupt distinction, the version and component checks) can be tested without a
    /// real file system underneath it. Every operation completes on the calling thread.
    /// </summary>
    /// <remarks>
    /// <c>FileStore</c> has its own fixture in <c>FileStoreTests</c> for what only a real file
    /// system can prove.
    /// See docs/testing.md, "The save fixtures".
    /// </remarks>
    public class FakeSaveStore : ISaveStore
    {
        private readonly Dictionary<string, byte[]> _files = new();

        private (string key, byte[] bytes, UniTaskCompletionSource completion)? _held;

        /// <summary>Writes that actually landed. A write that failed, or is still held, has not.</summary>
        public int WriteCount { get; private set; }

        /// <summary>
        /// The next this-many writes throw <c>SaveException.Io</c> and store nothing, the way
        /// <c>FileStore</c> reports a full disk or a revoked permission. Each failure counts this
        /// down by one.
        /// </summary>
        public int FailNextWrites { get; set; }

        /// <summary>
        /// One shot: the next write parks, storing nothing, until the test settles it with
        /// <see cref="CompleteHeldWrite"/> or <see cref="FailHeldWrite"/>. The test settles it from
        /// its own thread, so nothing here leaves the calling thread.
        /// </summary>
        public bool HoldNextWrite { get; set; }

        /// <summary>True while a write is parked by <see cref="HoldNextWrite"/>.</summary>
        public bool HasHeldWrite => _held.HasValue;

        /// <summary>
        /// Puts <paramref name="bytes"/> under <paramref name="key"/> without going through
        /// <c>SaveAsync</c>, for envelopes <c>SaveService</c>'s own codec and protector could never
        /// have produced: a corrupt one, one from a different schema version, one naming a
        /// different codec or protector.
        /// </summary>
        public void Seed(string key, byte[] bytes) => _files[key] = bytes;

        public UniTask WriteAsync(string key, byte[] bytes, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            if (FailNextWrites > 0)
            {
                FailNextWrites--;
                throw SaveException.Io(key, new IOException("FakeSaveStore was told to fail this write"));
            }

            if (HoldNextWrite)
            {
                HoldNextWrite = false;
                UniTaskCompletionSource completion = new();
                _held = (key, bytes, completion);
                return completion.Task;
            }

            _files[key] = bytes;
            WriteCount++;
            return UniTask.CompletedTask;
        }

        /// <summary>Lands the held write, then lets whatever awaited it carry on.</summary>
        /// <exception cref="System.InvalidOperationException">When no write is held.</exception>
        public void CompleteHeldWrite()
        {
            (string key, byte[] bytes, UniTaskCompletionSource completion) = TakeHeldWrite();
            _files[key] = bytes;
            WriteCount++;
            completion.TrySetResult();
        }

        /// <summary>Fails the held write the same way <see cref="FailNextWrites"/> does, storing nothing.</summary>
        /// <exception cref="System.InvalidOperationException">When no write is held.</exception>
        public void FailHeldWrite()
        {
            (string key, _, UniTaskCompletionSource completion) = TakeHeldWrite();
            completion.TrySetException(SaveException.Io(key, new IOException("FakeSaveStore was told to fail the held write")));
        }

        private (string key, byte[] bytes, UniTaskCompletionSource completion) TakeHeldWrite()
        {
            if (!_held.HasValue) throw new System.InvalidOperationException("FakeSaveStore has no held write to settle");

            (string key, byte[] bytes, UniTaskCompletionSource completion) held = _held.Value;
            _held = null;
            return held;
        }

        public UniTask<byte[]> ReadAsync(string key, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return UniTask.FromResult(_files.TryGetValue(key, out byte[] bytes) ? bytes : null);
        }

        public UniTask<bool> ExistsAsync(string key, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return UniTask.FromResult(_files.ContainsKey(key));
        }

        public UniTask DeleteAsync(string key, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            _files.Remove(key);
            return UniTask.CompletedTask;
        }

        /// <summary>Always true: no member ever suspends onto another thread.</summary>
        public bool CompletesOnCallingThread => true;
    }
}
