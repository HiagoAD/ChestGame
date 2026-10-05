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
    /// <summary>
    /// Covers every <c>(SaveStorage, SaveCodec, SaveProtection)</c> triple
    /// <see cref="SaveServiceFactory.CreateFrom"/> can build, proving each one actually round-trips a
    /// save written by one instance and read back by a second, independently constructed instance of
    /// the same triple.
    /// </summary>
    /// <remarks>
    /// See docs/saving.md, "The shape, and what it copies", and docs/testing.md, "The save fixtures".
    /// </remarks>
    public class SaveServiceFactoryCrossProductTests
    {
        private string _key;
        private string _root;
        private string _prefsPrefix;

        private class Inventory
        {
            public string PlayerName;
            public int Coins;
            public List<string> Items;
        }

        [SetUp]
        public void SetUp()
        {
            _key = "profile-" + Guid.NewGuid();
            _root = Path.Combine(Path.GetTempPath(), "ChestGameSaveTests_" + Guid.NewGuid());
            _prefsPrefix = "ChestGameSaveTests." + Guid.NewGuid() + ".";
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
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

            ISaveService writer = SaveServiceFactory.CreateFrom(storage, codec, protection, SaveFactoryInputs.Defaults(_root, _prefsPrefix));
            SynchronousUniTask.Complete(writer.SaveAsync(_key, original, CancellationToken.None));

            ISaveService reader = SaveServiceFactory.CreateFrom(storage, codec, protection, SaveFactoryInputs.Defaults(_root, _prefsPrefix));
            Inventory loaded = SynchronousUniTask.Result(reader.LoadAsync<Inventory>(_key, CancellationToken.None));

            Assert.AreEqual(original.PlayerName, loaded.PlayerName);
            Assert.AreEqual(original.Coins, loaded.Coins);
            CollectionAssert.AreEqual(original.Items, loaded.Items);
        }
    }
}
