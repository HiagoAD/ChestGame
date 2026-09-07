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
    // CurrencyResourceBankSaveHandle over a real, temp-rooted AtomicFile/Json/None ISaveService -
    // the same shape GameLifetimeScope.RegisterCoreServices composes, built here by hand through
    // SaveComponentFactory directly so these tests never touch GameLifetimeScope, and therefore
    // never touch the developer's real Application.persistentDataPath or real PlayerPrefs entry.
    // See docs/saving.md, "Currency: the first real caller".
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

        // Wraps a real CurrencyResourceBankSaveHandle only to record the moment Save() runs
        // relative to whatever else a test appends to the same list - never a replacement for the
        // real handler's own logic, which every call here still reaches.
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

        // --- The structural guard (docs/saving.md, "Load() blocks") ----------------------------

        [Test]
        public void Constructor_OverAThreadHoppingComposition_ThrowsSynchronousLoadNeedsNonHoppingStore()
        {
            // ThreadHoppingStore wrapping a plain FakeSaveStore always hops (FakeSaveStore is not
            // IMainThreadOnlyStore), so CompletesOnCallingThread answers false without this test
            // ever needing a real thread hop to actually happen.
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

        // The composition-root guard's own exception contract - see docs/saving.md, "What ships,
        // and where the composition asserts its own constraints". GameLifetimeScope.RegisterCoreServices
        // hardcodes SaveStorage.AtomicFile for the currency store, so nothing reachable through its
        // own public parameters (currencySaveInputs, legacyCurrencyPlayerPrefsKey) can ever make
        // that registration's own ISaveFlushRegistry.Register call actually throw - this pins the
        // exception's own message contract instead, since the throw site itself is unreachable
        // without either a production change or a fake standing in for the real registration. See
        // this gate's report for the coverage gap named plainly.
        [Test]
        public void SchedulerCannotFlushBlockingException_NamesTheKey_AndMentionsFlushBlocking()
        {
            SaveException error = SaveException.SchedulerCannotFlushBlocking("currency");

            StringAssert.Contains("currency", error.Message);
            StringAssert.Contains("FlushBlocking", error.Message);
        }

        // --- Round trip (docs/saving.md, "Currency: the first real caller") --------------------

        [Test]
        public void AddSpendAndReload_RoundTripsThroughTheRealPipeline()
        {
            ISaveService service1 = NewCurrencySaveService(_root);
            SaveScheduler<CurrencySaveDocument> scheduler1 = new(service1, CurrencyResourceBankSaveHandle.SaveKey, new FakeGameClock());
            CurrencyManager manager1 = new(new CurrencyResourceBankSaveHandle(service1, scheduler1));

            manager1.AddCurrency(CurrencyType.Coins, 100, "test");
            manager1.AddCurrency(CurrencyType.Gems, 20, "test");
            Assert.IsTrue(manager1.TrySpendCurrency(CurrencyType.Coins, 30, "test"));

            // Forces the coalesced write durably to disk before the next manager reads it back -
            // safe here because this composition never hops (CompletesOnCallingThread == true).
            scheduler1.FlushBlocking();
            scheduler1.Dispose();

            Assert.AreEqual(70, manager1.GetCurrencyAmount(CurrencyType.Coins));
            Assert.AreEqual(20, manager1.GetCurrencyAmount(CurrencyType.Gems));

            // A fresh manager, fresh scheduler, fresh ISaveService instance - over the same root -
            // standing in for a process restart reading back what the previous process wrote.
            ISaveService service2 = NewCurrencySaveService(_root);
            SaveScheduler<CurrencySaveDocument> scheduler2 = new(service2, CurrencyResourceBankSaveHandle.SaveKey, new FakeGameClock());
            CurrencyManager manager2 = new(new CurrencyResourceBankSaveHandle(service2, scheduler2));

            Assert.AreEqual(70, manager2.GetCurrencyAmount(CurrencyType.Coins));
            Assert.AreEqual(20, manager2.GetCurrencyAmount(CurrencyType.Gems));

            scheduler2.Dispose();
        }

        // --- Coalescing does not change the balance (docs/saving.md, "Write coalescing") -------

        [Test]
        public void ABurstOfAddsAndSpends_CoalescesWithoutChangingTheFinalBalance()
        {
            ISaveService service = NewCurrencySaveService(_root);
            SaveScheduler<CurrencySaveDocument> scheduler = new(service, CurrencyResourceBankSaveHandle.SaveKey, new FakeGameClock());
            CurrencyManager manager = new(new CurrencyResourceBankSaveHandle(service, scheduler));

            for (int i = 0; i < 25; i++) manager.AddCurrency(CurrencyType.Coins, 10, "burst");
            for (int i = 0; i < 10; i++) manager.TrySpendCurrency(CurrencyType.Coins, 5, "burst");

            long expected = 25 * 10 - 10 * 5;

            // The clock was never advanced, so the coalescing window has not elapsed and nothing
            // has actually been written yet - proving the balance above came from ResourceBank's
            // own in-memory state, not from a write that already landed.
            Assert.IsTrue(scheduler.HasPendingWrite, "guard: the burst has to still be waiting on its coalescing window");
            Assert.IsFalse(scheduler.IsFlushing, "guard: nothing should be mid-write yet - every call above had to return immediately");
            Assert.AreEqual(expected, manager.GetCurrencyAmount(CurrencyType.Coins));

            // Save() never blocks: every one of the 35 calls above already returned by the time this
            // line runs, and forcing the one coalesced write through now must not change the value.
            Assert.DoesNotThrow(() => scheduler.FlushBlocking());
            Assert.AreEqual(expected, manager.GetCurrencyAmount(CurrencyType.Coins));

            ISaveService reloadService = NewCurrencySaveService(_root);
            CurrencySaveDocument reloaded = SynchronousUniTask.Result(
                reloadService.LoadAsync<CurrencySaveDocument>(CurrencyResourceBankSaveHandle.SaveKey, CancellationToken.None));

            Assert.AreEqual(expected, reloaded.ResourceAmount[CurrencyType.Coins],
                "the one coalesced write has to carry the final balance, not any intermediate one");

            scheduler.Dispose();
        }

        // --- Event-ordering asymmetry (docs/saving.md, "IResourceBankSaveHandler<T> is fully
        // synchronous") - pre-existing ResourceBank<T> behaviour, unpinned until now, that the
        // switch to write-behind could plausibly have disturbed. --------------------------------

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
