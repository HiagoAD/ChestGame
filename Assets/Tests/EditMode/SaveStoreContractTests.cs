using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Company.ChestGame.Saving;
using Company.ChestGame.Tests.Common;
using NUnit.Framework;

namespace Company.ChestGame.Tests.EditMode
{
    // FileStore and AtomicFileStore both delegate every key rule to SaveKeyPath (docs/saving.md,
    // "SaveKeyPath, and why the key logic is shared rather than mirrored"). FileStoreTests pins
    // those rules for FileStore and must stay unchanged; this fixture is the proof that
    // AtomicFileStore, which shares the same internal type, answers identically rather than merely
    // similarly - the same shape PrefabPoolTests uses to run one written contract across four pool
    // implementations. The round-trip lifecycle shared by both file-backed stores is pinned here
    // too, for the same reason: neither store owns that behaviour more than the other.
    public class SaveStoreContractTests
    {
        private string _root;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "ChestGameSaveTests_" + Guid.NewGuid());
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }

        // --- The implementations under test -------------------------------------------------

        public class StoreCase
        {
            private readonly string _name;
            private readonly Func<string, ISaveStore> _create;

            public StoreCase(string name, Func<string, ISaveStore> create)
            {
                _name = name;
                _create = create;
            }

            public ISaveStore Create(string root) => _create(root);

            public override string ToString() => _name;
        }

        private static readonly StoreCase FileStoreCase = new("FileStore", root => new FileStore(root));
        private static readonly StoreCase AtomicFileStoreCase = new("AtomicFileStore", root => new AtomicFileStore(root));

        private static IEnumerable<StoreCase> EveryFileBackedStore()
        {
            yield return FileStoreCase;
            yield return AtomicFileStoreCase;
        }

        // --- Key handling: every rule FileStoreTests pins for FileStore, held identically ----

        [TestCaseSource(nameof(EveryFileBackedStore))]
        public void WriteAsync_WithNoKey_ThrowsNoKey(StoreCase implementation)
        {
            ISaveStore store = implementation.Create(_root);

            SaveException nullKey = Assert.Throws<SaveException>(
                () => SynchronousUniTask.Complete(store.WriteAsync(null, new byte[] { 1 }, CancellationToken.None)));
            StringAssert.Contains("needs a key", nullKey.Message);

            SaveException emptyKey = Assert.Throws<SaveException>(
                () => SynchronousUniTask.Complete(store.WriteAsync(string.Empty, new byte[] { 1 }, CancellationToken.None)));
            StringAssert.Contains("needs a key", emptyKey.Message);
        }

        [TestCaseSource(nameof(EveryFileBackedStore))]
        public void WriteAsync_WithARootedKey_ThrowsKeyEscapesRoot(StoreCase implementation)
        {
            ISaveStore store = implementation.Create(_root);
            string rooted = Path.Combine(_root, "elsewhere");
            Assert.IsTrue(Path.IsPathRooted(rooted), "guard: the key under test has to actually be rooted");

            SaveException error = Assert.Throws<SaveException>(
                () => SynchronousUniTask.Complete(store.WriteAsync(rooted, new byte[] { 1 }, CancellationToken.None)));
            StringAssert.Contains("outside the store's root directory", error.Message);
        }

        [TestCaseSource(nameof(EveryFileBackedStore))]
        public void WriteAsync_WithAKeyContainingDotDot_ThrowsKeyEscapesRoot(StoreCase implementation)
        {
            ISaveStore store = implementation.Create(_root);

            SaveException error = Assert.Throws<SaveException>(
                () => SynchronousUniTask.Complete(store.WriteAsync("../escape", new byte[] { 1 }, CancellationToken.None)));
            StringAssert.Contains("outside the store's root directory", error.Message);
        }

        [TestCaseSource(nameof(EveryFileBackedStore))]
        public void WriteAsync_WithAKeyContainingAPathSeparator_ThrowsInvalidKey(StoreCase implementation)
        {
            ISaveStore store = implementation.Create(_root);

            SaveException error = Assert.Throws<SaveException>(
                () => SynchronousUniTask.Complete(store.WriteAsync("a/b", new byte[] { 1 }, CancellationToken.None)));
            StringAssert.Contains("cannot appear in a file name", error.Message);
        }

        [TestCaseSource(nameof(EveryFileBackedStore))]
        public void WriteAsync_WithAKeyContainingAnInvalidFilenameCharacter_ThrowsInvalidKey(StoreCase implementation)
        {
            // NUL is the character that makes the check ordering load-bearing: Mono's
            // Path.IsPathRooted throws an untyped ArgumentException on it. If SaveKeyPath ever ran
            // that check before the invalid-character check, for either store, this would fail with
            // the wrong exception type rather than SaveException.
            char invalid = Array.Find(Path.GetInvalidFileNameChars(), c => c != Path.DirectorySeparatorChar && c != Path.AltDirectorySeparatorChar);
            string key = "a" + invalid + "b";
            ISaveStore store = implementation.Create(_root);

            SaveException error = Assert.Throws<SaveException>(
                () => SynchronousUniTask.Complete(store.WriteAsync(key, new byte[] { 1 }, CancellationToken.None)));
            StringAssert.Contains("cannot appear in a file name", error.Message);
        }

        [TestCaseSource(nameof(EveryFileBackedStore))]
        public void ReadAsync_ExistsAsync_AndDeleteAsync_AlsoRejectABadKey(StoreCase implementation)
        {
            const string badKey = "a/b";
            ISaveStore store = implementation.Create(_root);

            Assert.Throws<SaveException>(
                () => SynchronousUniTask.Result(store.ReadAsync(badKey, CancellationToken.None)));
            Assert.Throws<SaveException>(
                () => SynchronousUniTask.Result(store.ExistsAsync(badKey, CancellationToken.None)));
            Assert.Throws<SaveException>(
                () => SynchronousUniTask.Complete(store.DeleteAsync(badKey, CancellationToken.None)));
        }

        [TestCaseSource(nameof(EveryFileBackedStore))]
        public void ARejectedKey_NeverResolvesToTheFileOfAnAcceptedOne(StoreCase implementation)
        {
            ISaveStore store = implementation.Create(_root);
            byte[] original = { 1, 2, 3 };
            SynchronousUniTask.Complete(store.WriteAsync("a_b", original, CancellationToken.None));

            Assert.Throws<SaveException>(
                () => SynchronousUniTask.Complete(store.WriteAsync("a/b", new byte[] { 9, 9, 9 }, CancellationToken.None)));

            byte[] stillThere = SynchronousUniTask.Result(store.ReadAsync("a_b", CancellationToken.None));
            CollectionAssert.AreEqual(original, stillThere, "the rejected write must not have landed on the accepted key's file");
        }

        // --- Round trip and lifecycle, identical for both file-backed stores -----------------

        [TestCaseSource(nameof(EveryFileBackedStore))]
        public void WriteAsync_ThenReadAsync_ReturnsIdenticalBytes(StoreCase implementation)
        {
            ISaveStore store = implementation.Create(_root);
            byte[] payload = { 0, 1, 2, 254, 255, 10, 13, 0 };

            SynchronousUniTask.Complete(store.WriteAsync("save", payload, CancellationToken.None));
            byte[] readBack = SynchronousUniTask.Result(store.ReadAsync("save", CancellationToken.None));

            CollectionAssert.AreEqual(payload, readBack);
        }

        [TestCaseSource(nameof(EveryFileBackedStore))]
        public void WriteAsync_WithAnEmptyArray_ReadsBackAnEmptyArray(StoreCase implementation)
        {
            ISaveStore store = implementation.Create(_root);

            SynchronousUniTask.Complete(store.WriteAsync("save", Array.Empty<byte>(), CancellationToken.None));
            byte[] readBack = SynchronousUniTask.Result(store.ReadAsync("save", CancellationToken.None));

            Assert.IsNotNull(readBack, "an empty save is still a save; it must read back as an empty array, not as absent");
            CollectionAssert.AreEqual(Array.Empty<byte>(), readBack);
        }

        [TestCaseSource(nameof(EveryFileBackedStore))]
        public void ExistsAsync_IsFalseBeforeAWrite_AndTrueAfter(StoreCase implementation)
        {
            ISaveStore store = implementation.Create(_root);

            Assert.IsFalse(SynchronousUniTask.Result(store.ExistsAsync("save", CancellationToken.None)));

            SynchronousUniTask.Complete(store.WriteAsync("save", new byte[] { 1 }, CancellationToken.None));

            Assert.IsTrue(SynchronousUniTask.Result(store.ExistsAsync("save", CancellationToken.None)));
        }

        [TestCaseSource(nameof(EveryFileBackedStore))]
        public void DeleteAsync_ThenExistsAsync_IsFalse(StoreCase implementation)
        {
            ISaveStore store = implementation.Create(_root);
            SynchronousUniTask.Complete(store.WriteAsync("save", new byte[] { 1 }, CancellationToken.None));

            SynchronousUniTask.Complete(store.DeleteAsync("save", CancellationToken.None));

            Assert.IsFalse(SynchronousUniTask.Result(store.ExistsAsync("save", CancellationToken.None)));
        }

        [TestCaseSource(nameof(EveryFileBackedStore))]
        public void DeleteAsync_OfAnAbsentKey_DoesNotThrow(StoreCase implementation)
        {
            ISaveStore store = implementation.Create(_root);

            Assert.DoesNotThrow(
                () => SynchronousUniTask.Complete(store.DeleteAsync("neverWritten", CancellationToken.None)));
        }

        [TestCaseSource(nameof(EveryFileBackedStore))]
        public void WriteAsync_CreatesTheRootDirectoryWhenAbsent(StoreCase implementation)
        {
            ISaveStore store = implementation.Create(_root);
            Assert.IsFalse(Directory.Exists(_root), "guard: SetUp names a root but never creates it");

            SynchronousUniTask.Complete(store.WriteAsync("save", new byte[] { 1 }, CancellationToken.None));

            Assert.IsTrue(Directory.Exists(_root));
        }

        // --- Cancellation: FileStore's own guard, held identically by AtomicFileStore ---------

        [TestCaseSource(nameof(EveryFileBackedStore))]
        public void EveryMethod_WithAnAlreadyCancelledToken_ThrowsOperationCanceledException(StoreCase implementation)
        {
            ISaveStore store = implementation.Create(_root);
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();

            Assert.Throws<OperationCanceledException>(
                () => SynchronousUniTask.Complete(store.WriteAsync("save", new byte[] { 1 }, cancellation.Token)));
            Assert.Throws<OperationCanceledException>(
                () => SynchronousUniTask.Result(store.ReadAsync("save", cancellation.Token)));
            Assert.Throws<OperationCanceledException>(
                () => SynchronousUniTask.Result(store.ExistsAsync("save", cancellation.Token)));
            Assert.Throws<OperationCanceledException>(
                () => SynchronousUniTask.Complete(store.DeleteAsync("save", cancellation.Token)));

            Assert.IsFalse(Directory.Exists(_root),
                "a cancelled write must not have gotten far enough to create the root directory");
        }
    }
}
