using System;
using System.IO;
using System.Threading;
using Company.ChestGame.Currency;
using Company.ChestGame.Saving;
using Company.ChestGame.Tests.Common;
using NUnit.Framework;
using UnityEngine;

namespace Company.ChestGame.Tests.EditMode
{
    /// <summary>
    /// The legacy import, end to end, through the real adapter types this phase adds -
    /// <see cref="CurrencyLegacyImport"/>, <see cref="CurrencyResourceBankSaveHandle"/>,
    /// <see cref="CurrencySaveDocument"/>, <see cref="SaveScheduler{T}"/> - composed by hand exactly
    /// the shape <c>GameLifetimeScope.RegisterCoreServices</c> composes, but never through
    /// <c>GameLifetimeScope</c> itself, so this never touches the developer's real
    /// <c>Application.persistentDataPath</c> or real <c>"ResourceBankSaveData_CurrencyType"</c>
    /// PlayerPrefs entry. Every root and legacy key below is unique per test and cleaned up in
    /// TearDown even on failure.
    /// </summary>
    /// <remarks>
    /// See docs/saving.md, "The legacy import: CurrencyLegacyImport".
    /// See docs/saving.md, "Currency: the first real caller".
    /// </remarks>
    public class CurrencyLegacyImportIntegrationTests
    {
        private string _root;
        private string _legacyKey;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "ChestGameSaveTests_" + Guid.NewGuid().ToString("N"));
            _legacyKey = "ChestGameSaveTests.Legacy." + Guid.NewGuid().ToString("N");
        }

        /// <remarks>
        /// See docs/saving.md, "The legacy import: CurrencyLegacyImport".
        /// </remarks>
        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);

            PlayerPrefs.DeleteKey(_legacyKey);

            PlayerPrefs.DeleteKey(_legacyKey + ".migrated");
            PlayerPrefs.Save();
        }

        private ISaveService NewService()
        {
            SaveFactoryInputs inputs = SaveFactoryInputs.Defaults(_root);

            return new SaveService(
                SaveComponentFactory.CreateCodec(SaveCodec.Json),
                SaveComponentFactory.CreateProtector(SaveProtection.None, inputs),
                SaveComponentFactory.CreateStore(SaveStorage.AtomicFile, inputs),
                migrator: null,
                legacyImport: new CurrencyLegacyImport(_legacyKey));
        }

        private static CurrencyManager NewManager(ISaveService service, out SaveScheduler<CurrencySaveDocument> scheduler)
        {
            scheduler = new SaveScheduler<CurrencySaveDocument>(service, CurrencyResourceBankSaveHandle.SaveKey, new FakeGameClock());
            return new CurrencyManager(new CurrencyResourceBankSaveHandle(service, scheduler));
        }

        private void SeedLegacyData(long coins, long gems) =>
            PlayerPrefs.SetString(_legacyKey, $"{{\"ResourceAmount\":{{\"Coins\":{coins},\"Gems\":{gems}}}}}");

        [Test]
        public void FirstResolve_WithNoLegacyDataPresent_ReadsAsAGenuineFirstRun_AndImportsNothing()
        {
            Assert.IsFalse(PlayerPrefs.HasKey(_legacyKey), "guard: nothing should be seeded under this test's own GUID key");

            ISaveService service = NewService();
            CurrencyManager manager = NewManager(service, out SaveScheduler<CurrencySaveDocument> scheduler);

            Assert.AreEqual(0, manager.GetCurrencyAmount(CurrencyType.Coins));
            Assert.AreEqual(0, manager.GetCurrencyAmount(CurrencyType.Gems));
            Assert.IsFalse(SynchronousUniTask.Result(service.ExistsAsync(CurrencyResourceBankSaveHandle.SaveKey, CancellationToken.None)),
                "a plain first run with nothing legacy present must not write anything");

            scheduler.Dispose();
        }

        [Test]
        public void FirstResolve_WithLegacyDataPresent_ImportsBalances_WritesARealSave_AndClearsTheOldKey()
        {
            SeedLegacyData(670, 180);
            PlayerPrefs.Save();

            ISaveService service = NewService();
            CurrencyManager manager = NewManager(service, out SaveScheduler<CurrencySaveDocument> scheduler);

            Assert.AreEqual(670, manager.GetCurrencyAmount(CurrencyType.Coins), "balances have to carry across identically");
            Assert.AreEqual(180, manager.GetCurrencyAmount(CurrencyType.Gems), "balances have to carry across identically");
            Assert.IsTrue(SynchronousUniTask.Result(service.ExistsAsync(CurrencyResourceBankSaveHandle.SaveKey, CancellationToken.None)),
                "a real save has to exist under the new key once the import runs");
            Assert.IsFalse(PlayerPrefs.HasKey(_legacyKey), "the old legacy key has to be cleared once the import succeeds");

            scheduler.Dispose();
        }

        [Test]
        public void SecondResolve_DoesNotReimport()
        {
            SeedLegacyData(50, 5);
            PlayerPrefs.Save();

            ISaveService service1 = NewService();
            CurrencyManager manager1 = NewManager(service1, out SaveScheduler<CurrencySaveDocument> scheduler1);
            Assert.AreEqual(50, manager1.GetCurrencyAmount(CurrencyType.Coins));
            scheduler1.Dispose();

            ISaveService service2 = NewService();
            CurrencyManager manager2 = NewManager(service2, out SaveScheduler<CurrencySaveDocument> scheduler2);

            Assert.AreEqual(50, manager2.GetCurrencyAmount(CurrencyType.Coins),
                "the second resolve has to read the real save that already exists, not import again");

            scheduler2.Dispose();
        }

        /// <remarks>
        /// See docs/saving.md, "The legacy import: CurrencyLegacyImport".
        /// </remarks>
        [Test]
        public void SecondResolve_WithTheLegacyKeyStillPresent_DoesNotReimport_AndDoesNotOverwriteANewerSave()
        {
            SeedLegacyData(670, 180);
            PlayerPrefs.Save();

            ISaveService service1 = NewService();
            CurrencyManager manager1 = NewManager(service1, out SaveScheduler<CurrencySaveDocument> scheduler1);
            Assert.AreEqual(670, manager1.GetCurrencyAmount(CurrencyType.Coins));

            manager1.AddCurrency(CurrencyType.Coins, 30, "post-import");
            scheduler1.FlushBlocking();
            scheduler1.Dispose();

            SeedLegacyData(670, 180);
            PlayerPrefs.Save();

            ISaveService service2 = NewService();
            CurrencyManager manager2 = NewManager(service2, out SaveScheduler<CurrencySaveDocument> scheduler2);

            Assert.AreEqual(700, manager2.GetCurrencyAmount(CurrencyType.Coins),
                "a value saved after the import must not be overwritten by stale legacy data still sitting under the old key");

            scheduler2.Dispose();
        }
    }
}
