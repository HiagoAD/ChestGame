using System;
using System.Collections;
using System.Threading;
using Company.ChestGame.Saving;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Company.ChestGame.Tests.PlayMode
{
    /// <summary>
    /// Proves in a real player loop that <c>ThreadHoppingStore</c> leaves the calling thread and
    /// returns to it, and that a main-thread-only store is left alone rather than hopped.
    /// </summary>
    /// <remarks>
    /// <c>ThreadHoppingStoreTests</c> covers what edit mode can without a player loop.
    /// See docs/saving.md, "The thread hop, and why it is not inside SaveService".
    /// </remarks>
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

        /// <remarks>
        /// See docs/saving.md, "The thread hop, and why it is not inside SaveService".
        /// </remarks>
        [UnityTest]
        public IEnumerator Write_CancelledAfterTheWrappedWriteHasStarted_IsNotReportedAsCancelled() => UniTask.ToCoroutine(async () =>
        {
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
