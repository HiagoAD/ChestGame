using System.Collections.Generic;
using System.Threading;
using Company.ChestGame.Saving;
using Cysharp.Threading.Tasks;

namespace Company.ChestGame.Tests.EditMode
{
    // The one ISaveStore fake in this suite marked IMainThreadOnlyStore, so a test can prove
    // ThreadHoppingStore leaves a store like PlayerPrefsStore alone rather than hopping it. See
    // docs/saving.md, "The thread hop". Everything else mirrors FakeSaveStore.
    public class FakeMainThreadOnlyStore : ISaveStore, IMainThreadOnlyStore
    {
        private readonly Dictionary<string, byte[]> _files = new();

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

        // Every member above is a plain dictionary operation wrapped in an already-completed
        // UniTask - nothing here ever suspends, so this is always true.
        public bool CompletesOnCallingThread => true;
    }
}
