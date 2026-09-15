using System.Collections.Generic;
using System.Threading;
using Company.ChestGame.Saving;
using Cysharp.Threading.Tasks;

namespace Company.ChestGame.Tests.EditMode
{
    /// <summary>
    /// The one <see cref="ISaveStore"/> test double in this suite marked
    /// <see cref="IMainThreadOnlyStore"/>. Otherwise identical to <see cref="FakeSaveStore"/>.
    /// </summary>
    /// <remarks>
    /// See docs/saving.md, "The thread hop, and why it is not inside SaveService".
    /// </remarks>
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

        /// <summary>
        /// Always <c>true</c>: every operation above is a dictionary access wrapped in an
        /// already-completed <see cref="UniTask"/>, so none of them ever suspends.
        /// </summary>
        public bool CompletesOnCallingThread => true;
    }
}
