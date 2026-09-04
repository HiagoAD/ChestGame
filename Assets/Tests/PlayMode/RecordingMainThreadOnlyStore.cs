using System.Threading;
using Company.ChestGame.Saving;
using Cysharp.Threading.Tasks;

namespace Company.ChestGame.Tests.PlayMode
{
    // The one ISaveStore fake in this suite marked IMainThreadOnlyStore - proves, against a real
    // player loop and real thread identity, that ThreadHoppingStore leaves a store like
    // PlayerPrefsStore alone rather than moving its calls to a worker thread. See docs/saving.md,
    // "The thread hop".
    public class RecordingMainThreadOnlyStore : ISaveStore, IMainThreadOnlyStore
    {
        public int WriteThreadId { get; private set; } = -1;
        public byte[] LastWrittenBytes { get; private set; }

        public UniTask WriteAsync(string key, byte[] bytes, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            WriteThreadId = Thread.CurrentThread.ManagedThreadId;
            LastWrittenBytes = bytes;
            return UniTask.CompletedTask;
        }

        public UniTask<byte[]> ReadAsync(string key, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return UniTask.FromResult(LastWrittenBytes);
        }

        public UniTask<bool> ExistsAsync(string key, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return UniTask.FromResult(LastWrittenBytes != null);
        }

        public UniTask DeleteAsync(string key, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            LastWrittenBytes = null;
            return UniTask.CompletedTask;
        }

        public bool CompletesOnCallingThread => true;
    }
}
