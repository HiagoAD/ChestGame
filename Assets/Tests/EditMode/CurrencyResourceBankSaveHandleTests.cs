using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        // relative to whatever else a test appends to the same list, and the coin balance it was
        // handed at that moment - never a replacement for the real handler's own logic, which every
        // call here still reaches.
        private class OrderRecordingHandler : IResourceBankSaveHandler<CurrencyType>
        {
            private readonly IResourceBankSaveHandler<CurrencyType> _inner;
            public readonly List<string> Events = new();
            public readonly List<long> SavedCoinBalances = new();

            public OrderRecordingHandler(IResourceBankSaveHandler<CurrencyType> inner) => _inner = inner;

            public void Save(ResourceBankState<CurrencyType> data)
            {
                Events.Add("Save");
                SavedCoinBalances.Add(data.ResourceAmount[CurrencyType.Coins]);
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

        // The composition-root guard, through the real throw site rather than the message factory
        // behind it. GameLifetimeScope.RegisterCoreServices hardcodes SaveStorage.AtomicFile for the
        // currency store, so nothing reachable through its own public parameters can make its
        // registration throw; what can be reached is the same SaveFlushRegistry.Register call over
        // the same scheduler type and key, composed over a store that hops - the wiring mistake the
        // guard exists to refuse before the one moment a pause flush would need it.
        [Test]
        public void RegisteringTheCurrencySchedulerOverAHoppingService_IsRefused_NamingTheKey()
        {
            ISaveService hoppingService = new SaveService(new FakeSaveCodec(), new NoProtection(), new ThreadHoppingStore(new FakeSaveStore()));
            using SaveScheduler<CurrencySaveDocument> scheduler = new(hoppingService, CurrencyResourceBankSaveHandle.SaveKey, new FakeGameClock());
            SaveFlushRegistry registry = new();

            SaveException error = Assert.Throws<SaveException>(() => registry.Register(scheduler));

            StringAssert.Contains($"'{CurrencyResourceBankSaveHandle.SaveKey}'", error.Message,
                "the refusal has to name which save was wired wrong");
            StringAssert.Contains("FlushBlocking", error.Message);
            CollectionAssert.IsEmpty(registry.Registered, "a refused scheduler must not have been registered anyway");
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

        // --- Save before notify (docs/saving.md, "IResourceBankSaveHandler<T> is fully
        // synchronous") - as vendored, ResourceBank<T> notifies before saving on add but saves
        // before notifying on spend. The order matters because a listener is arbitrary game code:
        // one that throws during an add-first-notify-later sequence takes the save down with it, and
        // the balance the player was just shown is never persisted. One contract for both
        // directions: the new state reaches the save handler before any callback runs. -----------

        [Test]
        public void TryAddResourceAmount_HandsTheNewStateToTheSaveHandler_BeforeAnyCallbackFires()
        {
            ISaveService service = NewCurrencySaveService(_root);
            using SaveScheduler<CurrencySaveDocument> scheduler = new(service, CurrencyResourceBankSaveHandle.SaveKey, new FakeGameClock());
            OrderRecordingHandler handler = new(new CurrencyResourceBankSaveHandle(service, scheduler));
            CurrencyManager manager = new(handler);

            manager.OnCurrencyCollected += (currency, amount, balance, source) => handler.Events.Add("Collected");
            manager.OnCurrencyChanged += (currency, amount, balance, source) => handler.Events.Add("Changed");

            manager.AddCurrency(CurrencyType.Coins, 5, "order-test");

            Assert.AreEqual(3, handler.Events.Count, $"one save and two callbacks expected, got [{string.Join(", ", handler.Events)}]");
            Assert.AreEqual("Save", handler.Events[0],
                $"the new balance has to be handed to the save handler before any listener runs, got [{string.Join(", ", handler.Events)}]");
            CollectionAssert.AreEquivalent(new[] { "Collected", "Changed" }, handler.Events.Skip(1));
            CollectionAssert.AreEqual(new[] { 5L }, handler.SavedCoinBalances, "what was saved has to be the new balance");
        }

        [Test]
        public void TryToSpendResource_HandsTheNewStateToTheSaveHandler_BeforeAnyCallbackFires()
        {
            ISaveService service = NewCurrencySaveService(_root);
            using SaveScheduler<CurrencySaveDocument> scheduler = new(service, CurrencyResourceBankSaveHandle.SaveKey, new FakeGameClock());
            OrderRecordingHandler handler = new(new CurrencyResourceBankSaveHandle(service, scheduler));
            CurrencyManager manager = new(handler);

            manager.AddCurrency(CurrencyType.Coins, 10, "seed");
            handler.Events.Clear();
            handler.SavedCoinBalances.Clear();

            manager.OnCurrencySpent += (currency, amount, balance, source) => handler.Events.Add("Spent");
            manager.OnCurrencyChanged += (currency, amount, balance, source) => handler.Events.Add("Changed");

            Assert.IsTrue(manager.TrySpendCurrency(CurrencyType.Coins, 4, "order-test"));

            Assert.AreEqual(3, handler.Events.Count, $"one save and two callbacks expected, got [{string.Join(", ", handler.Events)}]");
            Assert.AreEqual("Save", handler.Events[0],
                $"the new balance has to be handed to the save handler before any listener runs, got [{string.Join(", ", handler.Events)}]");
            CollectionAssert.AreEquivalent(new[] { "Spent", "Changed" }, handler.Events.Skip(1));
            CollectionAssert.AreEqual(new[] { 6L }, handler.SavedCoinBalances, "what was saved has to be the new balance");
        }

        [Test]
        public void ACollectedListenerThatThrows_DoesNotStopTheNewBalanceBeingSaved()
        {
            ISaveService service = NewCurrencySaveService(_root);
            using SaveScheduler<CurrencySaveDocument> scheduler = new(service, CurrencyResourceBankSaveHandle.SaveKey, new FakeGameClock());
            OrderRecordingHandler handler = new(new CurrencyResourceBankSaveHandle(service, scheduler));
            CurrencyManager manager = new(handler);

            manager.OnCurrencyCollected += (currency, amount, balance, source) =>
                throw new InvalidOperationException("a listener failing for its own reasons");

            try
            {
                manager.AddCurrency(CurrencyType.Coins, 25, "throwing-listener");
            }
            catch (InvalidOperationException)
            {
                // The listener's own failure. Whether it reaches this caller is not what this test
                // is about; whether the balance it was told about got saved is.
            }

            Assert.AreEqual(25, manager.GetCurrencyAmount(CurrencyType.Coins), "guard: the bank itself did take the coins");
            CollectionAssert.AreEqual(new[] { 25L }, handler.SavedCoinBalances,
                "a balance the bank already holds - and already announced - has to have reached the save handler, whatever the listener did");
            Assert.IsTrue(scheduler.HasPendingWrite, "and the handler has to have handed it on to be written");

            scheduler.FlushBlocking();
            CurrencySaveDocument reloaded = SynchronousUniTask.Result(
                NewCurrencySaveService(_root).LoadAsync<CurrencySaveDocument>(CurrencyResourceBankSaveHandle.SaveKey, CancellationToken.None));
            Assert.AreEqual(25, reloaded.ResourceAmount[CurrencyType.Coins], "the next session has to see the coins the player was given");
        }
    }
}
