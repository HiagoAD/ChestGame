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
    // A profile built in code, its private serialized fields driven through SerializedObject the
    // way the inspector would, proving SaveServiceFactory actually reads what the dropdowns write
    // rather than a value only a test's own reflection could see. See docs/saving.md,
    // "SaveServiceFactory" and "The three selection enums are append-only".
    public class SaveProfileSOTests
    {
        private const string Key = "profile";

        private SaveProfileSO _profile;
        private string _root;
        private string _prefsPrefix;

        private class TestState
        {
            public int Value;
        }

        [SetUp]
        public void SetUp()
        {
            _profile = ScriptableObject.CreateInstance<SaveProfileSO>();
            _root = Path.Combine(Path.GetTempPath(), "ChestGameSaveTests_" + Guid.NewGuid());
            _prefsPrefix = "ChestGameSaveTests." + Guid.NewGuid() + ".";
        }

        [TearDown]
        public void TearDown()
        {
            if (_profile != null) Object.DestroyImmediate(_profile);
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
        private static IEnumerable<SaveCodec> EveryCodec() => Enum.GetValues(typeof(SaveCodec)).Cast<SaveCodec>();
        private static IEnumerable<SaveProtection> EveryProtection() => Enum.GetValues(typeof(SaveProtection)).Cast<SaveProtection>();

        private void SetStorage(SaveStorage storage)
        {
            UnityEditor.SerializedObject serialized = new(_profile);
            serialized.FindProperty("_storage").enumValueIndex = (int)storage;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private void SetCodec(SaveCodec codec)
        {
            UnityEditor.SerializedObject serialized = new(_profile);
            serialized.FindProperty("_codec").enumValueIndex = (int)codec;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private void SetProtection(SaveProtection protection)
        {
            UnityEditor.SerializedObject serialized = new(_profile);
            serialized.FindProperty("_protection").enumValueIndex = (int)protection;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static string CodecIdFor(SaveCodec codec) =>
            codec switch
            {
                SaveCodec.Json => "json",
                SaveCodec.JsonPretty => "json-pretty",
                SaveCodec.JsonGzip => "json-gzip",
                _ => throw new ArgumentOutOfRangeException(nameof(codec), codec, "a new SaveCodec member needs an expected id here too")
            };

        private static string ProtectionIdFor(SaveProtection protection) =>
            protection switch
            {
                SaveProtection.None => "none",
                SaveProtection.Base64 => "base64",
                SaveProtection.Xor => "xor",
                SaveProtection.Hmac => "hmac",
                SaveProtection.Aes => "aes",
                _ => throw new ArgumentOutOfRangeException(nameof(protection), protection, "a new SaveProtection member needs an expected id here too")
            };

        // --- A profile authored for each storage drives the factory to the matching backend -----

        [TestCaseSource(nameof(EveryStorage))]
        public void AProfileAuthoredForAStorage_DrivesTheFactoryToTheMatchingBackend(SaveStorage storage)
        {
            SetStorage(storage);
            Assert.AreEqual(storage, _profile.Storage,
                "guard: SerializedObject has to have actually set the field this test means to drive");

            ISaveService service = SaveServiceFactory.Create(_profile, _root, _prefsPrefix);
            TestState state = new() { Value = 21 };

            SynchronousUniTask.Complete(service.SaveAsync(Key, state, CancellationToken.None));
            TestState loaded = SynchronousUniTask.Result(service.LoadAsync<TestState>(Key, CancellationToken.None));

            Assert.AreEqual(21, loaded.Value);
        }

        [Test]
        public void AProfileAuthoredForFile_IsBackedByFileStore_WhichNeverKeepsABackup()
        {
            SetStorage(SaveStorage.File);
            ISaveService service = SaveServiceFactory.Create(_profile, _root, _prefsPrefix);

            SynchronousUniTask.Complete(service.SaveAsync(Key, new TestState { Value = 1 }, CancellationToken.None));
            SynchronousUniTask.Complete(service.SaveAsync(Key, new TestState { Value = 2 }, CancellationToken.None));

            Assert.IsFalse(Directory.GetFiles(_root).Any(f => f.EndsWith(".bak", StringComparison.Ordinal)),
                "a profile authored for File must land on FileStore, not AtomicFileStore");
        }

        [Test]
        public void AProfileAuthoredForAtomicFile_IsBackedByAtomicFileStore_WhichKeepsABackup()
        {
            SetStorage(SaveStorage.AtomicFile);
            ISaveService service = SaveServiceFactory.Create(_profile, _root, _prefsPrefix);

            SynchronousUniTask.Complete(service.SaveAsync(Key, new TestState { Value = 1 }, CancellationToken.None));
            SynchronousUniTask.Complete(service.SaveAsync(Key, new TestState { Value = 2 }, CancellationToken.None));

            Assert.IsTrue(Directory.GetFiles(_root).Any(f => f.EndsWith(".bak", StringComparison.Ordinal)),
                "a profile authored for AtomicFile must land on AtomicFileStore, not plain FileStore");
        }

        // --- Codec and protector fields are read too, not just storage --------------------------

        [Test]
        public void AFreshlySerializedProfile_NamesJsonAndNoneInTheWrittenEnvelope()
        {
            // Index 0 in both enums, which is where a field lands before anyone touches its
            // dropdown - so this also pins that a freshly authored profile is usable as-is.
            SetStorage(SaveStorage.File);
            Assert.AreEqual(SaveCodec.Json, _profile.Codec);
            Assert.AreEqual(SaveProtection.None, _profile.Protection);

            ISaveService service = SaveServiceFactory.Create(_profile, _root, _prefsPrefix);
            SynchronousUniTask.Complete(service.SaveAsync(Key, new TestState { Value = 1 }, CancellationToken.None));

            string savedFile = Directory.GetFiles(_root).Single(f => !f.EndsWith(".bak") && !f.EndsWith(".tmp"));
            string json = File.ReadAllText(savedFile);
            StringAssert.Contains("\"codec\": \"json\"", json,
                "the profile's Codec field has to reach the envelope actually written to disk");
            StringAssert.Contains("\"prot\": \"none\"", json,
                "the profile's Protection field has to reach the envelope actually written to disk");
        }

        // --- Every SaveCodec and every SaveProtection, authored through the profile (coverage gap) ---
        //
        // AProfileAuthoredForAStorage_DrivesTheFactoryToTheMatchingBackend above proves round-tripping,
        // but a round trip alone cannot catch "CreateCodec/CreateProtector always falls back to its
        // default arm regardless of what the dropdown says", because the same (wrong) component would
        // then be used for both the save and the load and still agree with itself. These two instead
        // pin the id actually written into the envelope on disk against what each enum member is
        // supposed to produce - the same check AFreshlySerializedProfile_NamesJsonAndNoneInTheWrittenEnvelope
        // already runs for the default (Json, None) pair, extended to every member of both enums.

        [TestCaseSource(nameof(EveryCodec))]
        public void AProfileAuthoredForACodec_WritesThatCodecsIdIntoTheEnvelope(SaveCodec codec)
        {
            SetStorage(SaveStorage.File);
            SetCodec(codec);
            Assert.AreEqual(codec, _profile.Codec,
                "guard: SerializedObject has to have actually set the field this test means to drive");

            ISaveService service = SaveServiceFactory.Create(_profile, _root, _prefsPrefix);
            SynchronousUniTask.Complete(service.SaveAsync(Key, new TestState { Value = 1 }, CancellationToken.None));

            string savedFile = Directory.GetFiles(_root).Single(f => !f.EndsWith(".bak") && !f.EndsWith(".tmp"));
            string json = File.ReadAllText(savedFile);
            StringAssert.Contains($"\"codec\": \"{CodecIdFor(codec)}\"", json,
                $"a profile authored for {codec} has to reach SaveServiceFactory.CreateCodec and actually drive the matching arm, not silently fall back to Json's");
        }

        [TestCaseSource(nameof(EveryProtection))]
        public void AProfileAuthoredForAProtection_WritesThatProtectionsIdIntoTheEnvelope(SaveProtection protection)
        {
            SetStorage(SaveStorage.File);
            SetProtection(protection);
            Assert.AreEqual(protection, _profile.Protection,
                "guard: SerializedObject has to have actually set the field this test means to drive");

            ISaveService service = SaveServiceFactory.Create(_profile, _root, _prefsPrefix);
            SynchronousUniTask.Complete(service.SaveAsync(Key, new TestState { Value = 1 }, CancellationToken.None));

            string savedFile = Directory.GetFiles(_root).Single(f => !f.EndsWith(".bak") && !f.EndsWith(".tmp"));
            string json = File.ReadAllText(savedFile);
            StringAssert.Contains($"\"prot\": \"{ProtectionIdFor(protection)}\"", json,
                $"a profile authored for {protection} has to reach SaveServiceFactory.CreateProtector and actually drive the matching arm, not silently fall back to None's");
        }

        // Round trip too, for every codec and every protection the profile can author - not just
        // that the right id landed in the envelope, but that what comes back out is also correct.

        [TestCaseSource(nameof(EveryCodec))]
        public void AProfileAuthoredForACodec_StillRoundTrips(SaveCodec codec)
        {
            SetStorage(SaveStorage.File);
            SetCodec(codec);
            ISaveService service = SaveServiceFactory.Create(_profile, _root, _prefsPrefix);
            TestState state = new() { Value = 33 };

            SynchronousUniTask.Complete(service.SaveAsync(Key, state, CancellationToken.None));
            TestState loaded = SynchronousUniTask.Result(service.LoadAsync<TestState>(Key, CancellationToken.None));

            Assert.AreEqual(33, loaded.Value);
        }

        [TestCaseSource(nameof(EveryProtection))]
        public void AProfileAuthoredForAProtection_StillRoundTrips(SaveProtection protection)
        {
            SetStorage(SaveStorage.File);
            SetProtection(protection);
            ISaveService service = SaveServiceFactory.Create(_profile, _root, _prefsPrefix);
            TestState state = new() { Value = 44 };

            SynchronousUniTask.Complete(service.SaveAsync(Key, state, CancellationToken.None));
            TestState loaded = SynchronousUniTask.Result(service.LoadAsync<TestState>(Key, CancellationToken.None));

            Assert.AreEqual(44, loaded.Value);
        }
    }
}
