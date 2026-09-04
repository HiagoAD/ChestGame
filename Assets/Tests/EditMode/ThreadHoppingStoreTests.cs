using System;
using System.Threading;
using Company.ChestGame.Saving;
using Company.ChestGame.Tests.Common;
using NUnit.Framework;

namespace Company.ChestGame.Tests.EditMode
{
    // ThreadHoppingStore's actual hop cannot run here at all - Tests.Common.SynchronousUniTask
    // throws the instant a task is still Pending immediately after the call returns, which is
    // exactly what a real UniTask.RunOnThreadPool hop looks like from this call stack, by design.
    // See docs/saving.md, "The thread hop". What edit mode can still prove without a player loop:
    // the constructor guard, both answers CompletesOnCallingThread can give, and that wrapping an
    // IMainThreadOnlyStore delegates straight through every member without ever reaching the hop at
    // all - every call below resolves on this same call stack, exactly like every other ISaveStore
    // this assembly ships. The genuine hop, and a main-thread-only store's write actually landing on
    // the main thread, are proven against a real player loop instead - see
    // ThreadHoppingStorePlayModeTests.
    public class ThreadHoppingStoreTests
    {
        [Test]
        public void Constructor_WithNoInnerStore_ThrowsNoStore()
        {
            SaveException error = Assert.Throws<SaveException>(() => new ThreadHoppingStore(null));
            StringAssert.Contains("needs an ISaveStore", error.Message);
        }

        [Test]
        public void CompletesOnCallingThread_OverAnOrdinaryStore_IsFalse()
        {
            ThreadHoppingStore store = new(new FakeSaveStore());

            Assert.IsFalse(store.CompletesOnCallingThread,
                "wrapping a store that is not IMainThreadOnlyStore means every member hops, so this must be false");
        }

        [Test]
        public void CompletesOnCallingThread_OverAMainThreadOnlyStore_IsTrue()
        {
            ThreadHoppingStore store = new(new FakeMainThreadOnlyStore());

            Assert.IsTrue(store.CompletesOnCallingThread,
                "wrapping an IMainThreadOnlyStore means every member calls straight through and never hops");
        }

        [Test]
        public void OverAMainThreadOnlyStore_EveryMember_DelegatesWithoutEverSuspending()
        {
            FakeMainThreadOnlyStore inner = new();
            ThreadHoppingStore store = new(inner);

            // SynchronousUniTask.Complete/Result throw the moment a task is still Pending
            // immediately after the call returns - the exact signal a real hop would trip. None of
            // these four calls hops, so all four resolve on this same call stack.
            SynchronousUniTask.Complete(store.WriteAsync("key", new byte[] { 1, 2, 3 }, CancellationToken.None));
            byte[] readBack = SynchronousUniTask.Result(store.ReadAsync("key", CancellationToken.None));
            bool exists = SynchronousUniTask.Result(store.ExistsAsync("key", CancellationToken.None));
            SynchronousUniTask.Complete(store.DeleteAsync("key", CancellationToken.None));

            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, readBack);
            Assert.IsTrue(exists);
        }

        [Test]
        public void EveryMember_WithAnAlreadyCancelledToken_ThrowsOperationCanceledException()
        {
            ThreadHoppingStore store = new(new FakeMainThreadOnlyStore());
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();

            Assert.Throws<OperationCanceledException>(
                () => SynchronousUniTask.Complete(store.WriteAsync("key", new byte[] { 1 }, cancellation.Token)));
            Assert.Throws<OperationCanceledException>(
                () => SynchronousUniTask.Result(store.ReadAsync("key", cancellation.Token)));
            Assert.Throws<OperationCanceledException>(
                () => SynchronousUniTask.Result(store.ExistsAsync("key", cancellation.Token)));
            Assert.Throws<OperationCanceledException>(
                () => SynchronousUniTask.Complete(store.DeleteAsync("key", cancellation.Token)));
        }
    }
}
