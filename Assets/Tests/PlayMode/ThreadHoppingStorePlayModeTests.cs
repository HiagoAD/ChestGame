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
    // Both checks below are deterministic identity comparisons - which thread a write ran on -
    // never timing. Nothing here asserts on how long anything took.
    public class ThreadHoppingStorePlayModeTests
    {
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
