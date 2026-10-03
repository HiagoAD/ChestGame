using System;
using System.Collections;
using System.Threading;
using Company.ChestGame.Saving;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Company.ChestGame.Tests.PlayMode
{
    // ThreadHoppingStore's actual hop cannot be proven in edit mode at all - Tests.Common's
    // SynchronousUniTask fails loudly the instant anything really suspends, by design. Only a real
    // player loop and real thread identity can prove the hop genuinely leaves the calling thread,
    // and that a main-thread-only store is left alone rather than hopped. See docs/saving.md, "The
    // thread hop", and ThreadHoppingStoreTests for what edit mode already covers without either.
    //
    // The thread checks below are deterministic identity comparisons - which thread a call ran on -
    // never timing, and the one cancellation check waits on a counter rather than a clock. Nothing
    // here asserts on how long anything took.
    public class ThreadHoppingStorePlayModeTests
    {
        private static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(10);

        [UnityTest]
        public IEnumerator Write_OverAnOrdinaryStore_RunsOnAWorkerThread_AndReturnsControlToTheMainThread() => UniTask.ToCoroutine(async () =>
        {
            int mainThreadId = Thread.CurrentThread.ManagedThreadId;
            RecordingSaveStore inner = new();
            ThreadHoppingStore store = new(inner);

            await store.WriteAsync("key", new byte[] { 1, 2, 3 }, CancellationToken.None);

            Assert.AreEqual(1, inner.WriteThreadIds.Count, "guard: exactly one write must have reached the wrapped store");
            Assert.AreNotEqual(mainThreadId, inner.WriteThreadIds[0],
                "the wrapped store's write has to run on a thread that is not the one that called WriteAsync");
            Assert.AreEqual(mainThreadId, Thread.CurrentThread.ManagedThreadId,
                "control has to be back on the main thread once the awaited call completes");
        });

        [UnityTest]
        public IEnumerator Read_OverAnOrdinaryStore_RunsOnAWorkerThread_AndHandsBackItsBytesOnTheMainThread() => UniTask.ToCoroutine(async () =>
        {
            int mainThreadId = Thread.CurrentThread.ManagedThreadId;
            RecordingSaveStore inner = new();
            ThreadHoppingStore store = new(inner);
            await store.WriteAsync("key", new byte[] { 4, 5, 6 }, CancellationToken.None);

            byte[] read = await store.ReadAsync("key", CancellationToken.None);

            CollectionAssert.AreEqual(new byte[] { 4, 5, 6 }, read, "the hop has to carry the wrapped store's result back unchanged");
            Assert.AreEqual(1, inner.ReadThreadIds.Count, "guard: exactly one read must have reached the wrapped store");
            Assert.AreNotEqual(mainThreadId, inner.ReadThreadIds[0],
                "the wrapped store's read has to run on a thread that is not the one that called ReadAsync");
            Assert.AreEqual(mainThreadId, Thread.CurrentThread.ManagedThreadId,
                "control has to be back on the main thread once the awaited read completes");
        });

        [UnityTest]
        public IEnumerator Write_CancelledAfterTheWrappedWriteHasStarted_IsNotReportedAsCancelled() => UniTask.ToCoroutine(async () =>
        {
            // ISaveStore observes cancellation only before a write begins, never after, so a
            // cancelled call has changed nothing. The converse is what this pins: once the write
            // has reached the wrapped store, cancelling must not turn it into a reported
            // cancellation - the bytes are on their way to disk regardless, and a caller told
            // "cancelled" would believe they were not. This is why HopAsync hands RunOnThreadPool
            // CancellationToken.None instead of the caller's token.
            RecordingSaveStore inner = new();
            ThreadHoppingStore store = new(inner);
            using CancellationTokenSource cancellation = new();

            inner.ArmBlockingWrite();
            UniTask writing = store.WriteAsync("key", new byte[] { 1, 2, 3 }, cancellation.Token);

            await UniTask.WaitUntil(() => inner.WriteThreadIds.Count == 1,
                cancellationToken: new CancellationTokenSource(PollTimeout).Token);

            cancellation.Cancel();
            inner.ReleaseWrite();

            bool reportedCancelled = false;
            try
            {
                await writing;
            }
            catch (OperationCanceledException)
            {
                reportedCancelled = true;
            }

            Assert.IsFalse(reportedCancelled, "a write that had already reached the wrapped store must not be reported as cancelled");
            Assert.AreEqual(1, inner.WriteCount, "the write the caller cancelled too late has to have landed, exactly once");
        });

        [Test]
        public void Write_OverAMainThreadOnlyStore_NeverLeavesTheMainThread_AndCompletesSynchronously()
        {
            int mainThreadId = Thread.CurrentThread.ManagedThreadId;
            RecordingMainThreadOnlyStore inner = new();
            ThreadHoppingStore store = new(inner);

            UniTask task = store.WriteAsync("key", new byte[] { 1, 2, 3 }, CancellationToken.None);

            Assert.AreEqual(UniTaskStatus.Succeeded, task.Status,
                "a main-thread-only store must never hop, so this has to already be done by the time WriteAsync returns");
            task.GetAwaiter().GetResult();

            Assert.AreEqual(mainThreadId, inner.WriteThreadId,
                "the wrapped store's write has to have run on the same thread that called WriteAsync");
        }
    }
}
