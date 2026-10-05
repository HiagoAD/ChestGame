using System;
using System.Collections.Generic;
using System.Threading;
using Company.ChestGame.Saving;
using Company.ChestGame.Tests.Common;
using NUnit.Framework;
using UnityEngine;

namespace Company.ChestGame.Tests.EditMode
{
    /// <summary>
    /// Covers <see cref="PlayerPrefsStore"/> against the real PlayerPrefs, since that is the entire
    /// seam this store wraps. Every key this fixture writes is namespaced under a per-test-run GUID
    /// prefix and deleted in TearDown, including on failure, so a broken assertion never leaves a
    /// stray entry in the developer's real editor prefs.
    /// </summary>
    /// <remarks>
    /// See docs/saving.md, "PlayerPrefsStore, and why it base64s".
    /// See docs/testing.md, "The save suites never touch a real save".
    /// </remarks>
    public class PlayerPrefsStoreTests
    {
        private string _prefix;
        private PlayerPrefsStore _store;

        /// <summary>
        /// Every (prefix, key) pair this test intends to touch, recorded before the write that might
        /// throw so <see cref="TearDown"/> still cleans it up.
        /// </summary>
        /// <remarks>
        /// See docs/testing.md, "The save suites never touch a real save".
        /// </remarks>
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

            // A DeleteKey that is never saved does not persist in batch mode, which is how a leaked
            // key was first noticed - see docs/testing.md.
            PlayerPrefs.Save();
        }

        private void Track(string prefix, string key) => _touched.Add((prefix, key));

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
        public void WriteAsync_WithANullArray_ReadsBackAnEmptyArray()
        {
            // ISaveStore: "a null array is stored as empty" - present, and empty, never absent.
            const string key = "profile";
            Track(_prefix, key);

            SynchronousUniTask.Complete(_store.WriteAsync(key, null, CancellationToken.None));
            byte[] readBack = SynchronousUniTask.Result(_store.ReadAsync(key, CancellationToken.None));

            Assert.IsNotNull(readBack, "a null write is stored as empty; it must read back as an empty array, not as absent");
            CollectionAssert.IsEmpty(readBack);
            Assert.IsTrue(SynchronousUniTask.Result(_store.ExistsAsync(key, CancellationToken.None)));
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

        [Test]
        public void ReadAsync_WhenTheStoredStringIsNotValidBase64_ThrowsSaveExceptionRatherThanFormatException()
        {
            const string key = "corrupt";
            Track(_prefix, key);
            PlayerPrefs.SetString(_prefix + key, "not-valid-base64-!!!");
            PlayerPrefs.Save();

            SaveException error = Assert.Throws<SaveException>(
                () => SynchronousUniTask.Result(_store.ReadAsync(key, CancellationToken.None)));
            Assert.IsInstanceOf<FormatException>(error.InnerException,
                "the FormatException Convert.FromBase64String throws must be preserved as the inner exception, not swallowed");
        }

        [TestCase(null)]
        [TestCase("")]
        public void WriteAsync_WithNoKey_ThrowsNoKey(string key)
        {
            SaveException error = Assert.Throws<SaveException>(
                () => SynchronousUniTask.Complete(_store.WriteAsync(key, new byte[] { 1 }, CancellationToken.None)));
            StringAssert.Contains("needs a key", error.Message);
        }

        [TestCase(null)]
        [TestCase("")]
        public void Constructing_WithNoKeyPrefix_ThrowsNoKeyPrefix(string prefix)
        {
            SaveException error = Assert.Throws<SaveException>(() => new PlayerPrefsStore(prefix));
            StringAssert.Contains("needs a key prefix", error.Message);
        }

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
