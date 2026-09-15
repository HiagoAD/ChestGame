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
    /// <summary>
    /// Covers <see cref="SaveServiceFactory.Create"/> and <see cref="SaveServiceFactory.CreateFrom"/>,
    /// turning a profile, or a bare triple, into a working <see cref="ISaveService"/>.
    /// </summary>
    /// <remarks>
    /// See docs/saving.md, "SaveComponentFactory, SaveFactoryInputs and SaveServiceFactory", and
    /// docs/testing.md, "The save fixtures".
    /// </remarks>
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
            PlayerPrefs.DeleteKey(_prefsPrefix + Key);
            PlayerPrefs.Save();
        }

        private static IEnumerable<SaveStorage> EveryStorage() => Enum.GetValues(typeof(SaveStorage)).Cast<SaveStorage>();

        [TestCaseSource(nameof(EveryStorage))]
        public void CreateFrom_EveryStorageMember_RoundTripsThroughItsBackend(SaveStorage storage)
        {
            ISaveService service = SaveServiceFactory.CreateFrom(storage, SaveCodec.Json, SaveProtection.None, SaveFactoryInputs.Defaults(_root, _prefsPrefix));
            TestState state = new() { Value = 42 };

            SynchronousUniTask.Complete(service.SaveAsync(Key, state, CancellationToken.None));
            TestState loaded = SynchronousUniTask.Result(service.LoadAsync<TestState>(Key, CancellationToken.None));

            Assert.AreEqual(42, loaded.Value);
        }

        /// <remarks>
        /// See docs/testing.md, "The save fixtures".
        /// </remarks>
        [Test]
        public void CreateFrom_File_IsBackedByFileStore_WhichNeverKeepsABackup()
        {
            ISaveService service = SaveServiceFactory.CreateFrom(SaveStorage.File, SaveCodec.Json, SaveProtection.None, SaveFactoryInputs.Defaults(_root));

            SynchronousUniTask.Complete(service.SaveAsync(Key, new TestState { Value = 1 }, CancellationToken.None));
            SynchronousUniTask.Complete(service.SaveAsync(Key, new TestState { Value = 2 }, CancellationToken.None));

            Assert.IsFalse(Directory.GetFiles(_root).Any(f => f.EndsWith(".bak", StringComparison.Ordinal)),
                "SaveStorage.File must be backed by FileStore, which overwrites in place and never keeps a .bak generation");
        }

        [Test]
        public void CreateFrom_AtomicFile_IsBackedByAtomicFileStore_WhichKeepsABackupAfterASecondSave()
        {
            ISaveService service = SaveServiceFactory.CreateFrom(SaveStorage.AtomicFile, SaveCodec.Json, SaveProtection.None, SaveFactoryInputs.Defaults(_root));

            SynchronousUniTask.Complete(service.SaveAsync(Key, new TestState { Value = 1 }, CancellationToken.None));
            SynchronousUniTask.Complete(service.SaveAsync(Key, new TestState { Value = 2 }, CancellationToken.None));

            Assert.IsTrue(Directory.GetFiles(_root).Any(f => f.EndsWith(".bak", StringComparison.Ordinal)),
                "SaveStorage.AtomicFile must be backed by AtomicFileStore, which keeps the previous write as .bak");
        }

        [Test]
        public void CreateFrom_PlayerPrefs_HonoursTheGivenKeyPrefix()
        {
            ISaveService service = SaveServiceFactory.CreateFrom(SaveStorage.PlayerPrefs, SaveCodec.Json, SaveProtection.None, SaveFactoryInputs.Defaults(playerPrefsKeyPrefix: _prefsPrefix));

            SynchronousUniTask.Complete(service.SaveAsync(Key, new TestState { Value = 5 }, CancellationToken.None));

            Assert.IsTrue(PlayerPrefs.HasKey(_prefsPrefix + Key),
                "the prefix passed to CreateFrom has to be the one the store actually wrote its key under");
        }

        [Test]
        public void CreateFrom_File_WritesUnderTheGivenRootDirectory_NotTheDefault()
        {
            ISaveService service = SaveServiceFactory.CreateFrom(SaveStorage.File, SaveCodec.Json, SaveProtection.None, SaveFactoryInputs.Defaults(_root));

            SynchronousUniTask.Complete(service.SaveAsync(Key, new TestState { Value = 1 }, CancellationToken.None));

            Assert.IsTrue(Directory.Exists(_root) && Directory.GetFiles(_root).Length > 0,
                "the given rootDirectory must be where the file actually landed");
        }

        [Test]
        public void CreateFrom_WithAnOutOfRangeStorage_FallsBackToAWorkingFileBackedService()
        {
            ISaveService service = SaveServiceFactory.CreateFrom((SaveStorage)99, SaveCodec.Json, SaveProtection.None, SaveFactoryInputs.Defaults(_root));
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

        [Test]
        public void Create_WithANullProfile_ThrowsNoProfile()
        {
            SaveException error = Assert.Throws<SaveException>(() => SaveServiceFactory.Create(null));
            StringAssert.Contains("SaveProfileSO", error.Message);
        }

        /// <remarks>
        /// See docs/saving.md, "SaveComponentFactory, SaveFactoryInputs and SaveServiceFactory".
        /// </remarks>
        [Test]
        public void Create_WithADestroyedProfile_ThrowsNoProfile()
        {
            SaveProfileSO profile = ScriptableObject.CreateInstance<SaveProfileSO>();
            Object.DestroyImmediate(profile);

            SaveException error = Assert.Throws<SaveException>(() => SaveServiceFactory.Create(profile));
            StringAssert.Contains("SaveProfileSO", error.Message);
        }
    }
}
