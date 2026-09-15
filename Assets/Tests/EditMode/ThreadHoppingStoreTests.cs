using System;
using System.Threading;
using Company.ChestGame.Saving;
using Company.ChestGame.Tests.Common;
using NUnit.Framework;

namespace Company.ChestGame.Tests.EditMode
{
    /// <summary>
    /// Proves what edit mode can prove about <see cref="ThreadHoppingStore"/> without a player loop:
    /// the constructor guard, both answers <c>CompletesOnCallingThread</c> can give, and that
    /// wrapping an <see cref="IMainThreadOnlyStore"/> delegates straight through every member
    /// without ever reaching the hop.
    /// </summary>
    /// <remarks>
    /// The genuine hop, and a main-thread-only store's write actually landing on the main thread,
    /// are proven against a real player loop instead by <c>ThreadHoppingStorePlayModeTests</c>.
    /// See docs/saving.md, "The thread hop, and why it is not inside SaveService".
    /// </remarks>
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
