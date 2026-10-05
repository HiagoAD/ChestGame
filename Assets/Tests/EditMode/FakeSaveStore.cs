using System.Collections.Generic;
using System.IO;
using System.Threading;
using Company.ChestGame.Saving;
using Cysharp.Threading.Tasks;

namespace Company.ChestGame.Tests.EditMode
{
    // An in-memory ISaveStore, so SaveService's own logic - the first-run/corrupt distinction, the
    // version and component checks - can be tested without a real file system underneath it.
    // FileStore gets its own fixture in FileStoreTests for what only a real file system can prove.
    public class FakeSaveStore : ISaveStore
    {
        private readonly Dictionary<string, byte[]> _files = new();

        private (string key, byte[] bytes, UniTaskCompletionSource completion)? _held;

        // Writes that actually landed. A write that failed, or is still held, has not.
        public int WriteCount { get; private set; }

        // The next this-many writes throw SaveException.Io and store nothing - the way FileStore
        // reports a full disk or a revoked permission - each one counting itself down.
        public int FailNextWrites { get; set; }

        // One shot: the next write parks, storing nothing, until the test settles it with
        // CompleteHeldWrite or FailHeldWrite. That is what makes "state marked dirty while a write
        // is in flight" reachable without a thread. The test settles it from its own thread, so
        // nothing here ever leaves the calling thread and CompletesOnCallingThread stays honest.
        public bool HoldNextWrite { get; set; }

        public bool HasHeldWrite => _held.HasValue;

        // Bypasses SaveAsync, for tests that need an envelope on "disk" that SaveService's own
        // codec and protector could never have produced - a corrupt one, one from a different
        // schema version, one naming a different codec or protector.
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

        // Lands the held write, then lets whatever awaited it carry on.
        public void CompleteHeldWrite()
        {
            (string key, byte[] bytes, UniTaskCompletionSource completion) = TakeHeldWrite();
            _files[key] = bytes;
            WriteCount++;
            completion.TrySetResult();
        }

        // Fails the held write the same way FailNextWrites does, storing nothing.
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

        // Every member above is a plain dictionary operation wrapped in an already-completed
        // UniTask - nothing here ever suspends onto another thread, so this is always true,
        // matching every real ISaveStore this assembly ships except ThreadHoppingStore.
        public bool CompletesOnCallingThread => true;
    }
}
