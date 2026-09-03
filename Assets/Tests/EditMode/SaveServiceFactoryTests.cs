using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Company.ChestGame.Saving;
using Company.ChestGame.Tests.Common;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Company.ChestGame.Tests.EditMode
{
    // SaveServiceFactory turns a profile, or a bare triple, into a working ISaveService. Every
    // file-backed case gets its own temp root and every PlayerPrefs case its own GUID prefix, both
    // torn down unconditionally, so a factory test never risks the developer's real save directory
    // or real editor prefs. See docs/saving.md, "SaveServiceFactory, and why every switch has a
    // working default arm".
    public class SaveServiceFactoryTests
    {
        private const string Key = "profile";

        private string _root;
        private string _prefsPrefix;

        private class TestState
        {
            public int Value;
        }

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "ChestGameSaveTests_" + Guid.NewGuid());
            _prefsPrefix = "ChestGameSaveTests." + Guid.NewGuid() + ".";
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
            // DeleteKey alone only edits PlayerPrefs' in-memory table; PlayerPrefsStore.
            // WriteAsync/DeleteAsync always follow a mutation with Save() for exactly this
            // reason. Without it here, a batch-mode process that exits before its own implicit
            // flush leaves this test's key sitting in the developer's real editor prefs even
            // though this TearDown ran and asked for it to be gone.
            PlayerPrefs.DeleteKey(_prefsPrefix + Key);
            PlayerPrefs.Save();
        }

        private static IEnumerable<SaveStorage> EveryStorage() => Enum.GetValues(typeof(SaveStorage)).Cast<SaveStorage>();

        // --- Every SaveStorage member actually round-trips (property 6) -----------------------

        [TestCaseSource(nameof(EveryStorage))]
        public void CreateFrom_EveryStorageMember_RoundTripsThroughItsBackend(SaveStorage storage)
        {
            ISaveService service = SaveServiceFactory.CreateFrom(storage, SaveCodec.Json, SaveProtection.None, _root, _prefsPrefix);
            TestState state = new() { Value = 42 };

            SynchronousUniTask.Complete(service.SaveAsync(Key, state, CancellationToken.None));
            TestState loaded = SynchronousUniTask.Result(service.LoadAsync<TestState>(Key, CancellationToken.None));

            Assert.AreEqual(42, loaded.Value);
        }

        // File and AtomicFile both leave the round trip above passing even if the factory wired the
        // wrong one of the two in, since both are files under _root. These two prove which backend
        // actually landed by checking for the .bak generation only AtomicFileStore ever writes.

        [Test]
        public void CreateFrom_File_IsBackedByFileStore_WhichNeverKeepsABackup()
        {
            ISaveService service = SaveServiceFactory.CreateFrom(SaveStorage.File, SaveCodec.Json, SaveProtection.None, _root);

            SynchronousUniTask.Complete(service.SaveAsync(Key, new TestState { Value = 1 }, CancellationToken.None));
            SynchronousUniTask.Complete(service.SaveAsync(Key, new TestState { Value = 2 }, CancellationToken.None));

            Assert.IsFalse(Directory.GetFiles(_root).Any(f => f.EndsWith(".bak", StringComparison.Ordinal)),
                "SaveStorage.File must be backed by FileStore, which overwrites in place and never keeps a .bak generation");
        }

        [Test]
        public void CreateFrom_AtomicFile_IsBackedByAtomicFileStore_WhichKeepsABackupAfterASecondSave()
        {
            ISaveService service = SaveServiceFactory.CreateFrom(SaveStorage.AtomicFile, SaveCodec.Json, SaveProtection.None, _root);

            SynchronousUniTask.Complete(service.SaveAsync(Key, new TestState { Value = 1 }, CancellationToken.None));
            SynchronousUniTask.Complete(service.SaveAsync(Key, new TestState { Value = 2 }, CancellationToken.None));

            Assert.IsTrue(Directory.GetFiles(_root).Any(f => f.EndsWith(".bak", StringComparison.Ordinal)),
                "SaveStorage.AtomicFile must be backed by AtomicFileStore, which keeps the previous write as .bak");
        }

        [Test]
        public void CreateFrom_PlayerPrefs_HonoursTheGivenKeyPrefix()
        {
            ISaveService service = SaveServiceFactory.CreateFrom(SaveStorage.PlayerPrefs, SaveCodec.Json, SaveProtection.None, playerPrefsKeyPrefix: _prefsPrefix);

            SynchronousUniTask.Complete(service.SaveAsync(Key, new TestState { Value = 5 }, CancellationToken.None));

            Assert.IsTrue(PlayerPrefs.HasKey(_prefsPrefix + Key),
                "the prefix passed to CreateFrom has to be the one the store actually wrote its key under");
        }

        // --- rootDirectory is honoured, not just accepted (property 6) ------------------------

        [Test]
        public void CreateFrom_File_WritesUnderTheGivenRootDirectory_NotTheDefault()
        {
            ISaveService service = SaveServiceFactory.CreateFrom(SaveStorage.File, SaveCodec.Json, SaveProtection.None, _root);

            SynchronousUniTask.Complete(service.SaveAsync(Key, new TestState { Value = 1 }, CancellationToken.None));

            Assert.IsTrue(Directory.Exists(_root) && Directory.GetFiles(_root).Length > 0,
                "the given rootDirectory must be where the file actually landed");
        }

        // --- Out-of-range enum values fall back to a working default (property 6) -------------

        [Test]
        public void CreateFrom_WithAnOutOfRangeStorage_FallsBackToAWorkingFileBackedService()
        {
            ISaveService service = SaveServiceFactory.CreateFrom((SaveStorage)99, SaveCodec.Json, SaveProtection.None, _root);
            TestState state = new() { Value = 7 };

            SynchronousUniTask.Complete(service.SaveAsync(Key, state, CancellationToken.None));
            TestState loaded = SynchronousUniTask.Result(service.LoadAsync<TestState>(Key, CancellationToken.None));

            Assert.AreEqual(7, loaded.Value);
            Assert.IsTrue(Directory.Exists(_root) && Directory.GetFiles(_root).Length > 0,
                "the fallback has to be a real file-backed store under the given root, not a silent no-op");
        }

        [Test]
        public void CreateFrom_WithAnOutOfRangeCodec_FallsBackToAWorkingCodec()
        {
            ISaveService service = SaveServiceFactory.CreateFrom(SaveStorage.InMemory, (SaveCodec)99, SaveProtection.None);
            TestState state = new() { Value = 11 };

            SynchronousUniTask.Complete(service.SaveAsync(Key, state, CancellationToken.None));
            TestState loaded = SynchronousUniTask.Result(service.LoadAsync<TestState>(Key, CancellationToken.None));

            Assert.AreEqual(11, loaded.Value);
        }

        [Test]
        public void CreateFrom_WithAnOutOfRangeProtection_FallsBackToAWorkingProtector()
        {
            ISaveService service = SaveServiceFactory.CreateFrom(SaveStorage.InMemory, SaveCodec.Json, (SaveProtection)99);
            TestState state = new() { Value = 13 };

            SynchronousUniTask.Complete(service.SaveAsync(Key, state, CancellationToken.None));
            TestState loaded = SynchronousUniTask.Result(service.LoadAsync<TestState>(Key, CancellationToken.None));

            Assert.AreEqual(13, loaded.Value);
        }

        // --- A missing profile (property 6) ----------------------------------------------------

        [Test]
        public void Create_WithANullProfile_ThrowsNoProfile()
        {
            SaveException error = Assert.Throws<SaveException>(() => SaveServiceFactory.Create(null));
            StringAssert.Contains("SaveProfileSO", error.Message);
        }

        [Test]
        public void Create_WithADestroyedProfile_ThrowsNoProfile()
        {
            // Unity-null rather than C#-null: a destroyed ScriptableObject still compiles as a
            // non-null reference. Only the overloaded `== null` operator, not `is null`, catches it.
            SaveProfileSO profile = ScriptableObject.CreateInstance<SaveProfileSO>();
            Object.DestroyImmediate(profile);

            SaveException error = Assert.Throws<SaveException>(() => SaveServiceFactory.Create(profile));
            StringAssert.Contains("SaveProfileSO", error.Message);
        }
    }
}
