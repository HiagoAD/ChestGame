using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Company.ChestGame.Currency;
using Company.ChestGame.Saving;
using Company.ChestGame.Tests.Common;
using NUnit.Framework;
using TapNation.Modules.ResourceBank.Saving;

namespace Company.ChestGame.Tests.EditMode
{
    /// <summary>
    /// <see cref="CurrencyResourceBankSaveHandle"/> over a real, temp-rooted AtomicFile/Json/None
    /// <see cref="ISaveService"/> - the same shape <c>GameLifetimeScope.RegisterCoreServices</c>
    /// composes, built here by hand through <see cref="SaveComponentFactory"/> directly so these
    /// tests never touch <c>GameLifetimeScope</c>, and therefore never touch the developer's real
    /// <c>Application.persistentDataPath</c> or real PlayerPrefs entry.
    /// </summary>
    /// <remarks>
    /// See docs/saving.md, "Currency: the first real caller".
    /// </remarks>
    public class CurrencyResourceBankSaveHandleTests
    {
        private string _root;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "ChestGameSaveTests_" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }

        private static ISaveService NewCurrencySaveService(string root, ISaveStore storeOverride = null)
        {
            SaveFactoryInputs inputs = SaveFactoryInputs.Defaults(root);
            ISaveStore store = storeOverride ?? SaveComponentFactory.CreateStore(SaveStorage.AtomicFile, inputs);

            return new SaveService(
                SaveComponentFactory.CreateCodec(SaveCodec.Json),
                SaveComponentFactory.CreateProtector(SaveProtection.None, inputs),
                store);
        }

        /// <summary>
        /// Wraps a real <see cref="CurrencyResourceBankSaveHandle"/> only to record the moment
        /// <see cref="Save"/> runs relative to whatever else a test appends to the same list - never
        /// a replacement for the real handler's own logic, which every call here still reaches.
        /// </summary>
        private class OrderRecordingHandler : IResourceBankSaveHandler<CurrencyType>
        {
            private readonly IResourceBankSaveHandler<CurrencyType> _inner;
            public readonly List<string> Events = new();

            public OrderRecordingHandler(IResourceBankSaveHandler<CurrencyType> inner) => _inner = inner;

            public void Save(ResourceBankState<CurrencyType> data)
            {
                Events.Add("Save");
                _inner.Save(data);
            }

            public ResourceBankState<CurrencyType> Load() => _inner.Load();
        }

        /// <remarks>
        /// See docs/saving.md, "The thread hop, and why it is not inside SaveService".
        /// </remarks>
        [Test]
        public void Constructor_OverAThreadHoppingComposition_ThrowsSynchronousLoadNeedsNonHoppingStore()
        {
            ISaveService hoppingService = new SaveService(new FakeSaveCodec(), new NoProtection(), new ThreadHoppingStore(new FakeSaveStore()));
            Assert.IsFalse(hoppingService.CompletesOnCallingThread, "guard: this composition has to be the hopping one this test means to drive");

            using SaveScheduler<CurrencySaveDocument> scheduler = new(hoppingService, "currency-guard-test", new FakeGameClock());

            SaveException error = Assert.Throws<SaveException>(() => new CurrencyResourceBankSaveHandle(hoppingService, scheduler));
            StringAssert.Contains("completes on the calling thread", error.Message);
        }

        [Test]
        public void Constructor_OverANonHoppingComposition_DoesNotThrow()
        {
            ISaveService service = NewCurrencySaveService(_root);
            using SaveScheduler<CurrencySaveDocument> scheduler = new(service, CurrencyResourceBankSaveHandle.SaveKey, new FakeGameClock());

            Assert.DoesNotThrow(() => new CurrencyResourceBankSaveHandle(service, scheduler));
        }

        [Test]
        public void Constructor_WithNoSaveService_Throws()
        {
            ISaveService service = NewCurrencySaveService(_root);
            using SaveScheduler<CurrencySaveDocument> scheduler = new(service, CurrencyResourceBankSaveHandle.SaveKey, new FakeGameClock());

            SaveException error = Assert.Throws<SaveException>(() => new CurrencyResourceBankSaveHandle(null, scheduler));
            StringAssert.Contains("ISaveService", error.Message);
        }

        [Test]
        public void Constructor_WithNoScheduler_Throws()
        {
            ISaveService service = NewCurrencySaveService(_root);

            SaveException error = Assert.Throws<SaveException>(() => new CurrencyResourceBankSaveHandle(service, null));
            StringAssert.Contains("SaveScheduler", error.Message);
        }

        /// <remarks>
        /// See docs/saving.md, "What ships, and where the composition asserts its own constraints".
        /// </remarks>
        [Test]
        public void SchedulerCannotFlushBlockingException_NamesTheKey_AndMentionsFlushBlocking()
        {
            SaveException error = SaveException.SchedulerCannotFlushBlocking("currency");

            StringAssert.Contains("currency", error.Message);
            StringAssert.Contains("FlushBlocking", error.Message);
        }

        /// <remarks>
        /// See docs/saving.md, "FlushBlocking, and why it cannot deadlock".
        /// </remarks>
        [Test]
        public void AddSpendAndReload_RoundTripsThroughTheRealPipeline()
        {
            ISaveService service1 = NewCurrencySaveService(_root);
            SaveScheduler<CurrencySaveDocument> scheduler1 = new(service1, CurrencyResourceBankSaveHandle.SaveKey, new FakeGameClock());
            CurrencyManager manager1 = new(new CurrencyResourceBankSaveHandle(service1, scheduler1));

            manager1.AddCurrency(CurrencyType.Coins, 100, "test");
            manager1.AddCurrency(CurrencyType.Gems, 20, "test");
            Assert.IsTrue(manager1.TrySpendCurrency(CurrencyType.Coins, 30, "test"));

            scheduler1.FlushBlocking();
            scheduler1.Dispose();

            Assert.AreEqual(70, manager1.GetCurrencyAmount(CurrencyType.Coins));
            Assert.AreEqual(20, manager1.GetCurrencyAmount(CurrencyType.Gems));

            ISaveService service2 = NewCurrencySaveService(_root);
            SaveScheduler<CurrencySaveDocument> scheduler2 = new(service2, CurrencyResourceBankSaveHandle.SaveKey, new FakeGameClock());
            CurrencyManager manager2 = new(new CurrencyResourceBankSaveHandle(service2, scheduler2));

            Assert.AreEqual(70, manager2.GetCurrencyAmount(CurrencyType.Coins));
            Assert.AreEqual(20, manager2.GetCurrencyAmount(CurrencyType.Gems));

            scheduler2.Dispose();
        }

        /// <remarks>
        /// See docs/saving.md, "Write coalescing, and why it cannot live inside SaveAsync".
        /// See docs/saving.md, "IResourceBankSaveHandler&lt;T&gt; is fully synchronous; ISaveService is not".
        /// </remarks>
        [Test]
        public void ABurstOfAddsAndSpends_CoalescesWithoutChangingTheFinalBalance()
        {
            ISaveService service = NewCurrencySaveService(_root);
            SaveScheduler<CurrencySaveDocument> scheduler = new(service, CurrencyResourceBankSaveHandle.SaveKey, new FakeGameClock());
            CurrencyManager manager = new(new CurrencyResourceBankSaveHandle(service, scheduler));

            for (int i = 0; i < 25; i++) manager.AddCurrency(CurrencyType.Coins, 10, "burst");
            for (int i = 0; i < 10; i++) manager.TrySpendCurrency(CurrencyType.Coins, 5, "burst");

            long expected = 25 * 10 - 10 * 5;

            Assert.IsTrue(scheduler.HasPendingWrite, "guard: the burst has to still be waiting on its coalescing window");
            Assert.IsFalse(scheduler.IsFlushing, "guard: nothing should be mid-write yet - every call above had to return immediately");
            Assert.AreEqual(expected, manager.GetCurrencyAmount(CurrencyType.Coins));

            Assert.DoesNotThrow(() => scheduler.FlushBlocking());
            Assert.AreEqual(expected, manager.GetCurrencyAmount(CurrencyType.Coins));

            ISaveService reloadService = NewCurrencySaveService(_root);
            CurrencySaveDocument reloaded = SynchronousUniTask.Result(
                reloadService.LoadAsync<CurrencySaveDocument>(CurrencyResourceBankSaveHandle.SaveKey, CancellationToken.None));

            Assert.AreEqual(expected, reloaded.ResourceAmount[CurrencyType.Coins],
                "the one coalesced write has to carry the final balance, not any intermediate one");

            scheduler.Dispose();
        }

        /// <remarks>
        /// See docs/saving.md, "The save-then-notify ordering no longer means what it used to".
        /// </remarks>
        [Test]
        public void TryAddResourceAmount_FiresItsCallback_BeforeSaving()
        {
            ISaveService service = NewCurrencySaveService(_root);
            using SaveScheduler<CurrencySaveDocument> scheduler = new(service, CurrencyResourceBankSaveHandle.SaveKey, new FakeGameClock());
            OrderRecordingHandler handler = new(new CurrencyResourceBankSaveHandle(service, scheduler));
            CurrencyManager manager = new(handler);

            manager.OnCurrencyCollected += (currency, amount, balance, source) => handler.Events.Add("Callback");

            manager.AddCurrency(CurrencyType.Coins, 5, "order-test");

            CollectionAssert.AreEqual(new[] { "Callback", "Save" }, handler.Events);
        }

        /// <remarks>
        /// See docs/saving.md, "The save-then-notify ordering no longer means what it used to".
        /// </remarks>
        [Test]
        public void TryToSpendResource_SavesBeforeFiringItsCallback()
        {
            ISaveService service = NewCurrencySaveService(_root);
            using SaveScheduler<CurrencySaveDocument> scheduler = new(service, CurrencyResourceBankSaveHandle.SaveKey, new FakeGameClock());
            OrderRecordingHandler handler = new(new CurrencyResourceBankSaveHandle(service, scheduler));
            CurrencyManager manager = new(handler);

            manager.AddCurrency(CurrencyType.Coins, 10, "seed");
            handler.Events.Clear();

            manager.OnCurrencySpent += (currency, amount, balance, source) => handler.Events.Add("Callback");

            Assert.IsTrue(manager.TrySpendCurrency(CurrencyType.Coins, 4, "order-test"));

            CollectionAssert.AreEqual(new[] { "Save", "Callback" }, handler.Events);
        }
    }
}
