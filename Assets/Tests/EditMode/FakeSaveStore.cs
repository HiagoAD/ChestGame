using System.Collections.Generic;
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

        // Bypasses SaveAsync, for tests that need an envelope on "disk" that SaveService's own
        // codec and protector could never have produced - a corrupt one, one from a different
        // schema version, one naming a different codec or protector.
        public void Seed(string key, byte[] bytes) => _files[key] = bytes;

        public UniTask WriteAsync(string key, byte[] bytes, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            _files[key] = bytes;
            return UniTask.CompletedTask;
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
    }
}
