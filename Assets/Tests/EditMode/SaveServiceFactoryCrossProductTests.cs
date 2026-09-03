using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Company.ChestGame.Saving;
using Company.ChestGame.Tests.Common;
using NUnit.Framework;
using UnityEngine;

namespace Company.ChestGame.Tests.EditMode
{
    // Phase 3's headline claim: every (SaveStorage, SaveCodec, SaveProtection) triple
    // SaveServiceFactory.CreateFrom can build actually round-trips a save written by one instance
    // and read back by a second, independently constructed instance of the same triple. All three
    // enums are walked with Enum.GetValues so a fifth storage, a fourth codec or a sixth protector
    // joins this test the moment it is appended, with nothing here needing to change. See
    // docs/saving.md, "The shape, and what it copies" and "SaveServiceFactory".
    public class SaveServiceFactoryCrossProductTests
    {
        private string _key;
        private string _root;
        private string _prefsPrefix;

        // Deliberately more than a bare int: a string, a number and a list, so a codec that only
        // happened to round-trip a single scalar field would not quietly pass this.
        private class Inventory
        {
            public string PlayerName;
            public int Coins;
            public List<string> Items;
        }

        [SetUp]
        public void SetUp()
        {
            // A unique key per test case, not a shared constant: SaveServiceFactory now hands every
            // SaveStorage.InMemory case the same process-lifetime InMemoryStore (so that two
            // separately-constructed InMemory-backed services actually see each other's writes -
            // the fix for the defect this file's cross-product test caught). That store now
            // outlives any single test, so two cases sharing one key would leak into each other and
            // the failure would depend on run order. File/AtomicFile already get a fresh _root per
            // test and PlayerPrefs a fresh _prefsPrefix; this key is this fixture's own isolation
            // for the one backend neither of those covers.
            _key = "profile-" + Guid.NewGuid();
            _root = Path.Combine(Path.GetTempPath(), "ChestGameSaveTests_" + Guid.NewGuid());
            _prefsPrefix = "ChestGameSaveTests." + Guid.NewGuid() + ".";
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
            // DeleteKey alone only edits PlayerPrefs' in-memory table; PlayerPrefsStore.
            // WriteAsync/DeleteAsync always follow a mutation with Save() for exactly this reason.
            // Without it here, a batch-mode process that exits before its own implicit flush leaves
            // this test's key sitting in the developer's real editor prefs even though this
            // TearDown ran and asked for it to be gone.
            PlayerPrefs.DeleteKey(_prefsPrefix + _key);
            PlayerPrefs.Save();
        }

        private static IEnumerable<object[]> EveryTriple()
        {
            foreach (SaveStorage storage in Enum.GetValues(typeof(SaveStorage)).Cast<SaveStorage>())
            foreach (SaveCodec codec in Enum.GetValues(typeof(SaveCodec)).Cast<SaveCodec>())
            foreach (SaveProtection protection in Enum.GetValues(typeof(SaveProtection)).Cast<SaveProtection>())
                yield return new object[] { storage, codec, protection };
        }

        [TestCaseSource(nameof(EveryTriple))]
        public void CreateFrom_EveryTriple_RoundTripsThroughASeparatelyConstructedService(
            SaveStorage storage, SaveCodec codec, SaveProtection protection)
        {
            Inventory original = new()
            {
                PlayerName = "Ada",
                Coins = 12345,
                Items = new List<string> { "sword", "shield", "potion" }
            };

            ISaveService writer = SaveServiceFactory.CreateFrom(storage, codec, protection, _root, _prefsPrefix);
            SynchronousUniTask.Complete(writer.SaveAsync(_key, original, CancellationToken.None));

            // A second, independently constructed service for the same triple - nothing here may
            // rely on the writer's own in-process state, only on what actually landed in the store.
            ISaveService reader = SaveServiceFactory.CreateFrom(storage, codec, protection, _root, _prefsPrefix);
            Inventory loaded = SynchronousUniTask.Result(reader.LoadAsync<Inventory>(_key, CancellationToken.None));

            Assert.AreEqual(original.PlayerName, loaded.PlayerName);
            Assert.AreEqual(original.Coins, loaded.Coins);
            CollectionAssert.AreEqual(original.Items, loaded.Items);
        }
    }
}
