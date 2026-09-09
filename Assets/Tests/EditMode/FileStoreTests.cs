using System;
using System.IO;
using System.Threading;
using Company.ChestGame.Saving;
using Company.ChestGame.Tests.Common;
using NUnit.Framework;
using UnityEngine;

namespace Company.ChestGame.Tests.EditMode
{
    // FileStore against a real file system, under a throwaway root created per test and removed in
    // TearDown regardless of outcome. Never Application.persistentDataPath and never
    // FileStore.DefaultRootDirectory(): the constructor takes a root precisely so a test never has
    // to touch a developer's real save folder. See docs/saving.md.
    public class FileStoreTests
    {
        private string _root;
        private FileStore _store;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "ChestGameSaveTests_" + Guid.NewGuid());
            _store = new FileStore(_root);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }

        // --- Key handling (property 5) -----------------------------------------------------------

        [TestCase(null)]
        [TestCase("")]
        public void WriteAsync_WithNoKey_ThrowsNoKey(string key)
        {
            SaveException error = Assert.Throws<SaveException>(
                () => SynchronousUniTask.Complete(_store.WriteAsync(key, new byte[] { 1 }, CancellationToken.None)));
            StringAssert.Contains("needs a key", error.Message);
        }

        [Test]
        public void WriteAsync_WithARootedKey_ThrowsKeyEscapesRoot()
        {
            string rooted = Path.Combine(_root, "elsewhere");
            Assert.IsTrue(Path.IsPathRooted(rooted), "guard: the key under test has to actually be rooted");

            SaveException error = Assert.Throws<SaveException>(
                () => SynchronousUniTask.Complete(_store.WriteAsync(rooted, new byte[] { 1 }, CancellationToken.None)));
            StringAssert.Contains("outside the store's root directory", error.Message);
        }

        [Test]
        public void WriteAsync_WithAKeyContainingDotDot_ThrowsKeyEscapesRoot()
        {
            SaveException error = Assert.Throws<SaveException>(
                () => SynchronousUniTask.Complete(_store.WriteAsync("../escape", new byte[] { 1 }, CancellationToken.None)));
            StringAssert.Contains("outside the store's root directory", error.Message);
        }

        [Test]
        public void WriteAsync_WithAKeyContainingAPathSeparator_ThrowsInvalidKey()
        {
            SaveException error = Assert.Throws<SaveException>(
                () => SynchronousUniTask.Complete(_store.WriteAsync("a/b", new byte[] { 1 }, CancellationToken.None)));
            StringAssert.Contains("cannot appear in a file name", error.Message);
        }

        [Test]
        public void WriteAsync_WithAKeyContainingAnInvalidFilenameCharacter_ThrowsInvalidKey()
        {
            // '/' is also an invalid file name character on this platform, so the case above alone
            // would not tell "rejects a separator" apart from "rejects anything in
            // GetInvalidFileNameChars". NUL is in that set and is not a separator, so this pins the
            // other half of the check.
            char invalid = Array.Find(Path.GetInvalidFileNameChars(), c => c != Path.DirectorySeparatorChar && c != Path.AltDirectorySeparatorChar);
            string key = "a" + invalid + "b";

            SaveException error = Assert.Throws<SaveException>(
                () => SynchronousUniTask.Complete(_store.WriteAsync(key, new byte[] { 1 }, CancellationToken.None)));
            StringAssert.Contains("cannot appear in a file name", error.Message);
        }

        [Test]
        public void ReadAsync_ExistsAsync_AndDeleteAsync_AlsoRejectABadKey()
        {
            // PathFor is one method shared by all four operations. This is not a repeat of the
            // WriteAsync cases above; it is the check that the other three actually go through that
            // same method rather than a copy of it that could drift.
            const string badKey = "a/b";

            Assert.Throws<SaveException>(
                () => SynchronousUniTask.Result(_store.ReadAsync(badKey, CancellationToken.None)));
            Assert.Throws<SaveException>(
                () => SynchronousUniTask.Result(_store.ExistsAsync(badKey, CancellationToken.None)));
            Assert.Throws<SaveException>(
                () => SynchronousUniTask.Complete(_store.DeleteAsync(badKey, CancellationToken.None)));
        }

        [Test]
        public void ARejectedKey_NeverResolvesToTheFileOfAnAcceptedOne()
        {
            // An earlier version sanitised unsafe characters to '_', which mapped a/b and a_b onto
            // the same file. This proves the replacement behaviour: rejection, not rewriting - the
            // bad key throws instead of silently landing on the good key's file.
            byte[] original = { 1, 2, 3 };
            SynchronousUniTask.Complete(_store.WriteAsync("a_b", original, CancellationToken.None));

            Assert.Throws<SaveException>(
                () => SynchronousUniTask.Complete(_store.WriteAsync("a/b", new byte[] { 9, 9, 9 }, CancellationToken.None)));

            byte[] stillThere = SynchronousUniTask.Result(_store.ReadAsync("a_b", CancellationToken.None));
            CollectionAssert.AreEqual(original, stillThere, "the rejected write must not have landed on the accepted key's file");
        }

        // --- Round trip and lifecycle (property 6) ------------------------------------------------

        [Test]
        public void WriteAsync_ThenReadAsync_ReturnsIdenticalBytes()
        {
            byte[] payload = { 0, 1, 2, 254, 255, 10, 13, 0 };

            SynchronousUniTask.Complete(_store.WriteAsync("save", payload, CancellationToken.None));
            byte[] readBack = SynchronousUniTask.Result(_store.ReadAsync("save", CancellationToken.None));

            CollectionAssert.AreEqual(payload, readBack);
        }

        [Test]
        public void ExistsAsync_IsFalseBeforeAWrite_AndTrueAfter()
        {
            Assert.IsFalse(SynchronousUniTask.Result(_store.ExistsAsync("save", CancellationToken.None)));

            SynchronousUniTask.Complete(_store.WriteAsync("save", new byte[] { 1 }, CancellationToken.None));

            Assert.IsTrue(SynchronousUniTask.Result(_store.ExistsAsync("save", CancellationToken.None)));
        }

        [Test]
        public void DeleteAsync_ThenExistsAsync_IsFalse()
        {
            SynchronousUniTask.Complete(_store.WriteAsync("save", new byte[] { 1 }, CancellationToken.None));

            SynchronousUniTask.Complete(_store.DeleteAsync("save", CancellationToken.None));

            Assert.IsFalse(SynchronousUniTask.Result(_store.ExistsAsync("save", CancellationToken.None)));
        }

        [Test]
        public void DeleteAsync_OfAnAbsentKey_DoesNotThrow()
        {
            Assert.DoesNotThrow(
                () => SynchronousUniTask.Complete(_store.DeleteAsync("neverWritten", CancellationToken.None)));
        }

        [Test]
        public void WriteAsync_CreatesTheRootDirectoryWhenAbsent()
        {
            Assert.IsFalse(Directory.Exists(_root), "guard: SetUp names a root but never creates it");

            SynchronousUniTask.Complete(_store.WriteAsync("save", new byte[] { 1 }, CancellationToken.None));

            Assert.IsTrue(Directory.Exists(_root));
        }

        // --- Cancellation (property 7) ------------------------------------------------------------

        [Test]
        public void EveryMethod_WithAnAlreadyCancelledToken_ThrowsOperationCanceledException()
        {
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();

            Assert.Throws<OperationCanceledException>(
                () => SynchronousUniTask.Complete(_store.WriteAsync("save", new byte[] { 1 }, cancellation.Token)));
            Assert.Throws<OperationCanceledException>(
                () => SynchronousUniTask.Result(_store.ReadAsync("save", cancellation.Token)));
            Assert.Throws<OperationCanceledException>(
                () => SynchronousUniTask.Result(_store.ExistsAsync("save", cancellation.Token)));
            Assert.Throws<OperationCanceledException>(
                () => SynchronousUniTask.Complete(_store.DeleteAsync("save", cancellation.Token)));

            Assert.IsFalse(Directory.Exists(_root),
                "a cancelled write must not have gotten far enough to create the root directory");
        }

        // --- DefaultRootDirectory: read, never used to build a store in these tests --------------

        [Test]
        public void DefaultRootDirectory_IsSavesUnderPersistentDataPath()
        {
            string expected = Path.Combine(Application.persistentDataPath, "Saves");

            Assert.AreEqual(expected, FileStore.DefaultRootDirectory());
        }
    }
}
