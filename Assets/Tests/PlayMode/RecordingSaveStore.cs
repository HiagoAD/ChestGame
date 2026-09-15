using System.Collections.Generic;
using System.Threading;
using Company.ChestGame.Saving;
using Cysharp.Threading.Tasks;

namespace Company.ChestGame.Tests.PlayMode
{
    /// <summary>
    /// An <see cref="ISaveStore"/> that records which managed thread each <see cref="WriteAsync"/>
    /// call ran on, and can park a write until the test releases it. The park is cooperative (an
    /// awaited <c>UniTaskCompletionSource</c>, not a real thread block), which is what lets the same
    /// fake drive both a non-hopping composition - where <see cref="WriteAsync"/> runs on the same
    /// main thread the test itself keeps running on - and a <c>ThreadHoppingStore</c>-wrapped one,
    /// without deadlocking either.
    /// </summary>
    /// <remarks>
    /// See docs/saving.md, "The thread hop, and why it is not inside SaveService".
    /// See docs/saving.md, "One write in flight".
    /// See docs/testing.md, "RecordingSaveStore, and why the gate is two fields, not one".
    /// </remarks>
    public class RecordingSaveStore : ISaveStore
    {
        /// <remarks>
        /// See docs/testing.md, "RecordingSaveStore, and why the gate is two fields, not one".
        /// </remarks>
        private UniTaskCompletionSource _armedGate;

        /// <remarks>
        /// See docs/testing.md, "RecordingSaveStore, and why the gate is two fields, not one".
        /// </remarks>
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
