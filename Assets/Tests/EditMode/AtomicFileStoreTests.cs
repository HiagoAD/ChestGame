using System;
using System.IO;
using System.Linq;
using System.Threading;
using Company.ChestGame.Saving;
using Company.ChestGame.Tests.Common;
using NUnit.Framework;

namespace Company.ChestGame.Tests.EditMode
{
    // What SaveStoreContractTests cannot: the generation behaviour that is the entire reason
    // AtomicFileStore exists rather than being FileStore. A live file is identified as whatever is
    // on disk that is neither .bak nor .tmp, so these tests never need to know the internal ".sav"
    // extension - only the ".bak" suffix docs/saving.md names is load-bearing here. See
    // docs/saving.md, "AtomicFileStore".
    public class AtomicFileStoreTests
    {
        private const string Key = "save";

        private string _root;
        private AtomicFileStore _store;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "ChestGameSaveTests_" + Guid.NewGuid());
            _store = new AtomicFileStore(_root);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }

        private void Write(byte[] bytes) =>
            SynchronousUniTask.Complete(_store.WriteAsync(Key, bytes, CancellationToken.None));

        private static string[] AllFiles(string root) =>
            Directory.Exists(root) ? Directory.GetFiles(root) : Array.Empty<string>();

        private static string[] BackupFiles(string root) =>
            AllFiles(root).Where(f => f.EndsWith(".bak", StringComparison.Ordinal)).ToArray();

        private static string[] LiveFiles(string root) =>
            AllFiles(root)
                .Where(f => !f.EndsWith(".bak", StringComparison.Ordinal) && !f.EndsWith(".tmp", StringComparison.Ordinal))
                .ToArray();

        private static string SingleBackupFile(string root)
        {
            string[] backups = BackupFiles(root);
            Assert.AreEqual(1, backups.Length, "expected exactly one .bak file on disk");
            return backups[0];
        }

        private static string SingleLiveFile(string root)
        {
            string[] live = LiveFiles(root);
            Assert.AreEqual(1, live.Length, "expected exactly one live file on disk");
            return live[0];
        }

        // --- Generations (property 2) ---------------------------------------------------------

        [Test]
        public void FirstWrite_LeavesNoBackupFile()
        {
            Write(new byte[] { 1 });

            Assert.IsEmpty(BackupFiles(_root), "a first write under a key has nothing previous to keep as .bak");
            Assert.AreEqual(1, LiveFiles(_root).Length);
        }

        [Test]
        public void SecondWrite_CreatesABackupHoldingTheFirstWritesBytes()
        {
            byte[] first = { 1, 2, 3 };
            byte[] second = { 4, 5, 6 };

            Write(first);
            Write(second);

            string backup = SingleBackupFile(_root);
            CollectionAssert.AreEqual(first, File.ReadAllBytes(backup));
            CollectionAssert.AreEqual(second, File.ReadAllBytes(SingleLiveFile(_root)));
        }

        [Test]
        public void ThirdWrite_LeavesOnlyOneGeneration_HoldingTheSecondWriteNotTheFirst()
        {
            byte[] first = { 1 };
            byte[] second = { 2 };
            byte[] third = { 3 };

            Write(first);
            Write(second);
            Write(third);

            string[] backups = BackupFiles(_root);
            Assert.AreEqual(1, backups.Length, "a third write must still leave exactly one backup generation, not one per write");
            CollectionAssert.AreEqual(second, File.ReadAllBytes(backups[0]),
                "the single surviving backup has to be the second write, not the first - a generation must roll rather than pile up");
            CollectionAssert.AreEqual(third, File.ReadAllBytes(SingleLiveFile(_root)));
        }

        // --- Read preferring live, falling back to .bak (property 2) --------------------------

        [Test]
        public void ReadAsync_ReturnsTheLiveFileWhenPresent()
        {
            Write(new byte[] { 1 });
            byte[] live = { 9, 9, 9 };
            Write(live);

            byte[] read = SynchronousUniTask.Result(_store.ReadAsync(Key, CancellationToken.None));

            CollectionAssert.AreEqual(live, read);
        }

        [Test]
        public void ReadAsync_WhenTheLiveFileIsDeletedButTheBackupRemains_ReturnsTheBackupsBytes()
        {
            // The fallback this class exists for: docs/saving.md is explicit that a missing live
            // file returning the .bak copy is the fallback doing its job, not a bug.
            byte[] first = { 1, 2, 3 };
            byte[] second = { 4, 5, 6 };
            Write(first);
            Write(second);
            File.Delete(SingleLiveFile(_root));

            byte[] read = SynchronousUniTask.Result(_store.ReadAsync(Key, CancellationToken.None));

            CollectionAssert.AreEqual(first, read, "with the live file gone, ReadAsync has to fall back to what .bak holds");
        }

        [Test]
        public void ExistsAsync_IsTrueWhenOnlyTheBackupRemains()
        {
            Write(new byte[] { 1 });
            Write(new byte[] { 2 });
            File.Delete(SingleLiveFile(_root));

            Assert.IsTrue(SynchronousUniTask.Result(_store.ExistsAsync(Key, CancellationToken.None)),
                "a key with only a .bak left under it is still a save that exists");
        }

        [Test]
        public void DeleteAsync_RemovesBothTheLiveFileAndTheBackup()
        {
            Write(new byte[] { 1 });
            Write(new byte[] { 2 });
            Assert.IsNotEmpty(BackupFiles(_root), "guard: there has to be a backup for this test to prove deleting it");

            SynchronousUniTask.Complete(_store.DeleteAsync(Key, CancellationToken.None));

            Assert.IsFalse(SynchronousUniTask.Result(_store.ExistsAsync(Key, CancellationToken.None)));
            Assert.IsEmpty(BackupFiles(_root));
            Assert.IsEmpty(LiveFiles(_root));
        }
    }
}
