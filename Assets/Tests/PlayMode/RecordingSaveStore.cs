using System.Collections.Generic;
using System.Threading;
using Company.ChestGame.Saving;
using Cysharp.Threading.Tasks;

namespace Company.ChestGame.Tests.PlayMode
{
    // An ISaveStore that records which managed thread each WriteAsync call ran on, and can park a
    // write until the test releases it. The park is cooperative (an awaited UniTaskCompletionSource,
    // not a real thread block), which is what lets the same fake drive both a non-hopping
    // composition - where WriteAsync runs on the same main thread the test itself keeps running on -
    // and a ThreadHoppingStore-wrapped one, without deadlocking either. This is what makes "a write
    // genuinely mid-flight" a deterministic state to assert against rather than a race against how
    // fast a worker thread happens to run. See docs/saving.md, "The thread hop" and "One write in
    // flight".
    public class RecordingSaveStore : ISaveStore
    {
        // Set by ArmBlockingWrite, consumed by the next WriteAsync call that starts waiting - one
        // shot, so a follow-up write in the same test completes immediately without a fresh Arm
        // call. Kept separate from _activeGate below: nulling this the moment a write claims it is
        // what makes a follow-up write not block again, but ReleaseWrite() has to keep working after
        // that point too, which is exactly what _activeGate is for.
        private UniTaskCompletionSource _armedGate;

        // The gate whatever write is currently parked is actually awaiting - what ReleaseWrite()
        // signals. Without this as its own field, ReleaseWrite() would read _armedGate after
        // WriteAsync has already cleared it to claim it, and release nothing.
        private UniTaskCompletionSource _activeGate;

        public int WriteCount { get; private set; }
        public byte[] LastWrittenBytes { get; private set; }
        public List<int> WriteThreadIds { get; } = new();

        public void ArmBlockingWrite() => _armedGate = new UniTaskCompletionSource();

        public void ReleaseWrite() => _activeGate?.TrySetResult();

        public async UniTask WriteAsync(string key, byte[] bytes, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            WriteThreadIds.Add(Thread.CurrentThread.ManagedThreadId);

            UniTaskCompletionSource gate = _armedGate;
            _armedGate = null;
            if (gate != null)
            {
                _activeGate = gate;
                await gate.Task;
            }

            WriteCount++;
            LastWrittenBytes = bytes;
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
