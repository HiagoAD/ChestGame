using System;
using System.Threading;
using Company.ChestGame.Saving;
using Company.ChestGame.Tests.Common;
using NUnit.Framework;

namespace Company.ChestGame.Tests.EditMode
{
    /// <summary>
    /// Covers <c>InMemoryStore</c>. Nothing here touches disk or PlayerPrefs, so there is no root
    /// or prefix to isolate and nothing to clean up in <c>TearDown</c>.
    /// </summary>
    /// <remarks>
    /// See docs/saving.md, "InMemoryStore".
    /// </remarks>
    public class InMemoryStoreTests
    {
        private const string Key = "save";

        [Test]
        public void WriteAsync_ThenReadAsync_ReturnsIdenticalBytes()
        {
            InMemoryStore store = new();
            byte[] payload = { 0, 1, 2, 254, 255 };

            SynchronousUniTask.Complete(store.WriteAsync(Key, payload, CancellationToken.None));
            byte[] readBack = SynchronousUniTask.Result(store.ReadAsync(Key, CancellationToken.None));

            CollectionAssert.AreEqual(payload, readBack);
        }

        [Test]
        public void WriteAsync_WithANullArray_ReadsBackAnEmptyArray()
        {
            InMemoryStore store = new();

            SynchronousUniTask.Complete(store.WriteAsync(Key, null, CancellationToken.None));
            byte[] readBack = SynchronousUniTask.Result(store.ReadAsync(Key, CancellationToken.None));

            Assert.IsNotNull(readBack, "a null write is stored as empty; it must read back as an empty array, not as absent");
            CollectionAssert.IsEmpty(readBack);
            Assert.IsTrue(SynchronousUniTask.Result(store.ExistsAsync(Key, CancellationToken.None)));
        }

        [Test]
        public void WriteAsync_MutatingTheCallersArrayAfterwards_DoesNotChangeWhatWasStored()
        {
            InMemoryStore store = new();
            byte[] original = { 1, 2, 3 };
            byte[] handedOff = (byte[])original.Clone();

            SynchronousUniTask.Complete(store.WriteAsync(Key, handedOff, CancellationToken.None));
            handedOff[0] = 99;

            byte[] stored = SynchronousUniTask.Result(store.ReadAsync(Key, CancellationToken.None));
            CollectionAssert.AreEqual(original, stored,
                "the store has to have cloned on the way in; mutating the caller's own array afterwards must not reach what it believes it saved");
        }

        [Test]
        public void ReadAsync_MutatingTheReturnedArray_DoesNotChangeWhatIsStoredForTheNextRead()
        {
            InMemoryStore store = new();
            byte[] original = { 1, 2, 3 };
            SynchronousUniTask.Complete(store.WriteAsync(Key, original, CancellationToken.None));

            byte[] firstRead = SynchronousUniTask.Result(store.ReadAsync(Key, CancellationToken.None));
            firstRead[0] = 99;

            byte[] secondRead = SynchronousUniTask.Result(store.ReadAsync(Key, CancellationToken.None));
            CollectionAssert.AreEqual(original, secondRead,
                "the store has to have cloned on the way out; mutating what a caller was handed must not reach the store's own copy");
        }

        [Test]
        public void ExistsAsync_IsFalseBeforeAWrite_AndTrueAfter()
        {
            InMemoryStore store = new();

            Assert.IsFalse(SynchronousUniTask.Result(store.ExistsAsync(Key, CancellationToken.None)));

            SynchronousUniTask.Complete(store.WriteAsync(Key, new byte[] { 1 }, CancellationToken.None));

            Assert.IsTrue(SynchronousUniTask.Result(store.ExistsAsync(Key, CancellationToken.None)));
        }

        [Test]
        public void DeleteAsync_ThenExistsAsync_IsFalse()
        {
            InMemoryStore store = new();
            SynchronousUniTask.Complete(store.WriteAsync(Key, new byte[] { 1 }, CancellationToken.None));

            SynchronousUniTask.Complete(store.DeleteAsync(Key, CancellationToken.None));

            Assert.IsFalse(SynchronousUniTask.Result(store.ExistsAsync(Key, CancellationToken.None)));
        }

        [Test]
        public void DeleteAsync_OfAnAbsentKey_DoesNotThrow()
        {
            InMemoryStore store = new();

            Assert.DoesNotThrow(
                () => SynchronousUniTask.Complete(store.DeleteAsync("neverWritten", CancellationToken.None)));
        }

        [TestCase(null)]
        [TestCase("")]
        public void WriteAsync_WithNoKey_ThrowsNoKey(string key)
        {
            InMemoryStore store = new();

            SaveException error = Assert.Throws<SaveException>(
                () => SynchronousUniTask.Complete(store.WriteAsync(key, new byte[] { 1 }, CancellationToken.None)));
            StringAssert.Contains("needs a key", error.Message);
        }

        [Test]
        public void EveryMethod_WithAnAlreadyCancelledToken_ThrowsOperationCanceledException()
        {
            InMemoryStore store = new();
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();

            Assert.Throws<OperationCanceledException>(
                () => SynchronousUniTask.Complete(store.WriteAsync(Key, new byte[] { 1 }, cancellation.Token)));
            Assert.Throws<OperationCanceledException>(
                () => SynchronousUniTask.Result(store.ReadAsync(Key, cancellation.Token)));
            Assert.Throws<OperationCanceledException>(
                () => SynchronousUniTask.Result(store.ExistsAsync(Key, cancellation.Token)));
            Assert.Throws<OperationCanceledException>(
                () => SynchronousUniTask.Complete(store.DeleteAsync(Key, cancellation.Token)));

            Assert.IsFalse(SynchronousUniTask.Result(store.ExistsAsync(Key, CancellationToken.None)),
                "a cancelled write must not have gotten far enough to actually store anything");
        }
    }
}
