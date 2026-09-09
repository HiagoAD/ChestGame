using System;
using System.Collections.Generic;
using System.Threading;
using Company.ChestGame.Saving;
using Company.ChestGame.Tests.Common;
using NUnit.Framework;
using UnityEngine;

namespace Company.ChestGame.Tests.EditMode
{
    // Against the real PlayerPrefs, since that is the entire seam this store wraps - never
    // PlayerPrefs.DeleteAll, and every key this fixture writes is namespaced under a
    // per-test-run GUID prefix and deleted in TearDown, including on failure, so a broken
    // assertion never leaves a stray entry in the developer's real editor prefs. See
    // docs/saving.md, "PlayerPrefsStore, and why it base64s".
    public class PlayerPrefsStoreTests
    {
        private string _prefix;
        private PlayerPrefsStore _store;

        // Every (prefix, key) pair this test intends to touch, recorded before the write that
        // might throw, so TearDown can clean up regardless of how the test ends. PlayerPrefs.DeleteKey
        // on a key that was never set is a no-op, so recording intent rather than only successes is safe.
        private readonly List<(string prefix, string key)> _touched = new();

        [SetUp]
        public void SetUp()
        {
            _prefix = "ChestGameSaveTests." + Guid.NewGuid() + ".";
            _store = new PlayerPrefsStore(_prefix);
        }

        [TearDown]
        public void TearDown()
        {
            foreach ((string prefix, string key) in _touched)
            {
                PlayerPrefs.DeleteKey(prefix + key);
            }
            _touched.Clear();
        }

        private void Track(string prefix, string key) => _touched.Add((prefix, key));

        // --- Round trip and HasKey semantics ---------------------------------------------------

        [Test]
        public void WriteAsync_ThenReadAsync_ReturnsIdenticalBytesThroughBase64()
        {
            const string key = "profile";
            Track(_prefix, key);
            byte[] payload = { 0, 1, 2, 254, 255 };

            SynchronousUniTask.Complete(_store.WriteAsync(key, payload, CancellationToken.None));
            byte[] readBack = SynchronousUniTask.Result(_store.ReadAsync(key, CancellationToken.None));

            CollectionAssert.AreEqual(payload, readBack);
        }

        [Test]
        public void ExistsAsync_IsFalseBeforeAWrite_AndTrueAfter()
        {
            const string key = "profile";
            Track(_prefix, key);

            Assert.IsFalse(SynchronousUniTask.Result(_store.ExistsAsync(key, CancellationToken.None)));

            SynchronousUniTask.Complete(_store.WriteAsync(key, new byte[] { 1 }, CancellationToken.None));

            Assert.IsTrue(SynchronousUniTask.Result(_store.ExistsAsync(key, CancellationToken.None)));
        }

        [Test]
        public void DeleteAsync_RemovesTheKey()
        {
            const string key = "profile";
            Track(_prefix, key);
            SynchronousUniTask.Complete(_store.WriteAsync(key, new byte[] { 1 }, CancellationToken.None));

            SynchronousUniTask.Complete(_store.DeleteAsync(key, CancellationToken.None));

            Assert.IsFalse(SynchronousUniTask.Result(_store.ExistsAsync(key, CancellationToken.None)));
        }

        // --- Corrupt value under an existing key (property 4) ----------------------------------

        [Test]
        public void ReadAsync_WhenTheStoredStringIsNotValidBase64_ThrowsSaveExceptionRatherThanFormatException()
        {
            const string key = "corrupt";
            Track(_prefix, key);
            // Written directly, bypassing WriteAsync: simulates a value that did not come from this
            // store, or one that has been hand-edited, rather than something WriteAsync itself
            // could ever produce.
            PlayerPrefs.SetString(_prefix + key, "not-valid-base64-!!!");
            PlayerPrefs.Save();

            SaveException error = Assert.Throws<SaveException>(
                () => SynchronousUniTask.Result(_store.ReadAsync(key, CancellationToken.None)));
            Assert.IsInstanceOf<FormatException>(error.InnerException,
                "the FormatException Convert.FromBase64String throws must be preserved as the inner exception, not swallowed");
        }

        // --- Key presence still applies (the one SaveKeyPath rule that carries over) -----------

        [TestCase(null)]
        [TestCase("")]
        public void WriteAsync_WithNoKey_ThrowsNoKey(string key)
        {
            SaveException error = Assert.Throws<SaveException>(
                () => SynchronousUniTask.Complete(_store.WriteAsync(key, new byte[] { 1 }, CancellationToken.None)));
            StringAssert.Contains("needs a key", error.Message);
        }

        // --- Constructing without a prefix ------------------------------------------------------

        [TestCase(null)]
        [TestCase("")]
        public void Constructing_WithNoKeyPrefix_ThrowsNoKeyPrefix(string prefix)
        {
            SaveException error = Assert.Throws<SaveException>(() => new PlayerPrefsStore(prefix));
            StringAssert.Contains("needs a key prefix", error.Message);
        }

        // --- The reason the prefix exists at all ------------------------------------------------

        [Test]
        public void TwoStoresWithDifferentPrefixes_DoNotSeeEachOthersKeys()
        {
            string otherPrefix = "ChestGameSaveTests." + Guid.NewGuid() + ".";
            PlayerPrefsStore other = new(otherPrefix);
            const string key = "shared-key-name";
            Track(_prefix, key);
            Track(otherPrefix, key);

            SynchronousUniTask.Complete(_store.WriteAsync(key, new byte[] { 1, 2, 3 }, CancellationToken.None));

            Assert.IsTrue(SynchronousUniTask.Result(_store.ExistsAsync(key, CancellationToken.None)));
            Assert.IsFalse(SynchronousUniTask.Result(other.ExistsAsync(key, CancellationToken.None)),
                "a different prefix must not see a key this store wrote under the same logical name");
            Assert.IsNull(SynchronousUniTask.Result(other.ReadAsync(key, CancellationToken.None)));

            SynchronousUniTask.Complete(other.WriteAsync(key, new byte[] { 9, 9, 9 }, CancellationToken.None));
            byte[] stillOriginal = SynchronousUniTask.Result(_store.ReadAsync(key, CancellationToken.None));

            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, stillOriginal,
                "writing the same logical key under a different prefix must not have overwritten this prefix's value");
        }

        // --- Cancellation: the same guard FileStoreTests pins for FileStore --------------------

        [Test]
        public void EveryMethod_WithAnAlreadyCancelledToken_ThrowsOperationCanceledException()
        {
            const string key = "save";
            Track(_prefix, key);
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();

            Assert.Throws<OperationCanceledException>(
                () => SynchronousUniTask.Complete(_store.WriteAsync(key, new byte[] { 1 }, cancellation.Token)));
            Assert.Throws<OperationCanceledException>(
                () => SynchronousUniTask.Result(_store.ReadAsync(key, cancellation.Token)));
            Assert.Throws<OperationCanceledException>(
                () => SynchronousUniTask.Result(_store.ExistsAsync(key, cancellation.Token)));
            Assert.Throws<OperationCanceledException>(
                () => SynchronousUniTask.Complete(_store.DeleteAsync(key, cancellation.Token)));

            Assert.IsFalse(PlayerPrefs.HasKey(_prefix + key),
                "a cancelled write must not have gotten far enough to actually set the key");
        }
    }
}
