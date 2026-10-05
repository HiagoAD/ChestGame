using System.Collections.Generic;
using System.Threading;
using Company.ChestGame.Saving;
using Cysharp.Threading.Tasks;

namespace Company.ChestGame.Tests.PlayMode
{
    /// <summary>
    /// An <see cref="ISaveStore"/> that records which managed thread each <c>WriteAsync</c> call ran
    /// on, and can park a write until the test releases it.
    /// </summary>
    /// <remarks>
    /// The park is cooperative (an awaited <c>UniTaskCompletionSource</c>, not a real thread block),
    /// so the same fake drives both a non-hopping composition, where <c>WriteAsync</c> runs on the
    /// main thread the test itself keeps running on, and a <c>ThreadHoppingStore</c>-wrapped one,
    /// without deadlocking either.
    /// Holds one value (the last bytes written) regardless of key.
    /// See docs/testing.md, "RecordingSaveStore, and why the gate is two fields, not one".
    /// </remarks>
    public class RecordingSaveStore : ISaveStore
    {
        private UniTaskCompletionSource _armedGate;

        private volatile UniTaskCompletionSource _activeGate;

        /// <summary>Number of writes that ran to completion; a parked write is not counted.</summary>
        public int WriteCount { get; private set; }

        /// <summary>The bytes of the last completed write, or null after a delete or before any write.</summary>
        public byte[] LastWrittenBytes { get; private set; }

        /// <summary>The managed thread id each <c>WriteAsync</c> call started on, in call order.</summary>
        public List<int> WriteThreadIds { get; } = new();

        /// <summary>The managed thread id each <c>ReadAsync</c> call ran on, in call order.</summary>
        public List<int> ReadThreadIds { get; } = new();

        /// <summary>
        /// Makes the next <c>WriteAsync</c> park until <see cref="ReleaseWrite"/>. One shot: a
        /// follow-up write completes immediately unless armed again.
        /// </summary>
        public void ArmBlockingWrite() => _armedGate = new UniTaskCompletionSource();

        /// <summary>
        /// Releases the write that is currently parked. Does nothing if no write has been parked.
        /// </summary>
        public void ReleaseWrite() => _activeGate?.TrySetResult();

        /// <summary>
        /// Records the calling thread, parks if a gate was armed, then stores <paramref name="bytes"/>.
        /// </summary>
        /// <remarks>
        /// The gate is published before the thread id is recorded, so a test that waits for
        /// <see cref="WriteThreadIds"/> to grow and then calls <see cref="ReleaseWrite"/> always
        /// releases the write it means to. Do not reorder.
        /// See docs/testing.md, "RecordingSaveStore, and why the gate is two fields, not one".
        /// </remarks>
        public async UniTask WriteAsync(string key, byte[] bytes, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            UniTaskCompletionSource gate = _armedGate;
            _armedGate = null;
            if (gate != null) _activeGate = gate;

            WriteThreadIds.Add(Thread.CurrentThread.ManagedThreadId);

            if (gate != null) await gate.Task;

            WriteCount++;
            LastWrittenBytes = bytes;
        }

        public UniTask<byte[]> ReadAsync(string key, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            ReadThreadIds.Add(Thread.CurrentThread.ManagedThreadId);
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
