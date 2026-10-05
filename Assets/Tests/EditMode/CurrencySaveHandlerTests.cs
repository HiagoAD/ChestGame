using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Company.ChestGame.Currency;
using Company.ChestGame.Saving;
using Company.ChestGame.Tests.Common;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Company.ChestGame.Tests.EditMode
{
    /// <summary>
    /// <c>CurrencySaveHandler</c> over a real, temp-rooted AtomicFile/Json/None
    /// <c>ISaveService</c>: the same shape <c>GameLifetimeScope.RegisterCoreServices</c> composes,
    /// built by hand through <c>SaveComponentFactory</c> directly. Touches neither
    /// <c>GameLifetimeScope</c>, the real <c>Application.persistentDataPath</c> nor the real
    /// PlayerPrefs entry.
    /// </summary>
    /// <remarks>
    /// See docs/saving.md, "Currency: the first real caller".
    /// </remarks>
    public class CurrencySaveHandlerTests
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
        /// Wraps a real <c>CurrencySaveHandler</c> to record when <c>Save()</c> runs relative to
        /// whatever else a test appends to <c>Events</c>, and the coin balance it was handed at that
        /// moment. Every call still reaches the wrapped handler.
        /// </summary>
        private class OrderRecordingHandler : ICurrencySaveHandler
        {
            private readonly ICurrencySaveHandler _inner;
            public readonly List<string> Events = new();
            public readonly List<long> SavedCoinBalances = new();

            public OrderRecordingHandler(ICurrencySaveHandler inner) => _inner = inner;

            public void Save(CurrencySaveDocument document)
            {
                Events.Add("Save");
                SavedCoinBalances.Add(document.ResourceAmount[CurrencyType.Coins]);
                _inner.Save(document);
            }

            public CurrencySaveDocument Load() => _inner.Load();
        }

        /// <summary>
        /// What the next process would read: a fresh service, scheduler and manager over the same
        /// root. A currency the file does not list reads as 0, so a missing file fails as a wrong
        /// balance rather than as a missing key.
        /// </summary>
        private long ReloadedBalance(CurrencyType currencyType)
        {
            ISaveService service = NewCurrencySaveService(_root);
            using SaveScheduler<CurrencySaveDocument> scheduler = new(service, CurrencySaveHandler.SaveKey, new FakeGameClock());

            return new CurrencyManager(new CurrencySaveHandler(service, scheduler)).GetCurrencyAmount(currencyType);
        }

        private static void SubscribeTo(CurrencyManager manager, string eventName, CurrencyChangedHandler listener)
        {
            switch (eventName)
            {
                case "Collected": manager.OnCurrencyCollected += listener; break;
                case "Spent": manager.OnCurrencySpent += listener; break;
                case "Changed": manager.OnCurrencyChanged += listener; break;
                default: throw new ArgumentOutOfRangeException(nameof(eventName), eventName, null);
            }
        }

        private static List<string> RecordEvents(CurrencyManager manager)
        {
            List<string> events = new();

            manager.OnCurrencyCollected += (currency, amount, balance, source) => events.Add("Collected");
            manager.OnCurrencySpent += (currency, amount, balance, source) => events.Add("Spent");
            manager.OnCurrencyChanged += (currency, amount, balance, source) => events.Add("Changed");

            return events;
        }

        /// <remarks>
        /// See docs/saving.md, "ICurrencySaveHandler is fully synchronous; ISaveService is not".
        /// </remarks>
        [Test]
        public void Constructor_OverAThreadHoppingComposition_ThrowsSynchronousLoadNeedsNonHoppingStore()
        {
            ISaveService hoppingService = new SaveService(new FakeSaveCodec(), new NoProtection(), new ThreadHoppingStore(new FakeSaveStore()));
            Assert.IsFalse(hoppingService.CompletesOnCallingThread, "guard: this composition has to be the hopping one this test means to drive");

            using SaveScheduler<CurrencySaveDocument> scheduler = new(hoppingService, "currency-guard-test", new FakeGameClock());

            SaveException error = Assert.Throws<SaveException>(() => new CurrencySaveHandler(hoppingService, scheduler));
            StringAssert.Contains("completes on the calling thread", error.Message);
        }

        [Test]
        public void Constructor_OverANonHoppingComposition_DoesNotThrow()
        {
            ISaveService service = NewCurrencySaveService(_root);
            using SaveScheduler<CurrencySaveDocument> scheduler = new(service, CurrencySaveHandler.SaveKey, new FakeGameClock());

            Assert.DoesNotThrow(() => new CurrencySaveHandler(service, scheduler));
        }

        [Test]
        public void Constructor_WithNoSaveService_Throws()
        {
            ISaveService service = NewCurrencySaveService(_root);
            using SaveScheduler<CurrencySaveDocument> scheduler = new(service, CurrencySaveHandler.SaveKey, new FakeGameClock());

            SaveException error = Assert.Throws<SaveException>(() => new CurrencySaveHandler(null, scheduler));
            StringAssert.Contains("ISaveService", error.Message);
        }

        [Test]
        public void Constructor_WithNoScheduler_Throws()
        {
            ISaveService service = NewCurrencySaveService(_root);

            SaveException error = Assert.Throws<SaveException>(() => new CurrencySaveHandler(service, null));
            StringAssert.Contains("SaveScheduler", error.Message);
        }

        /// <remarks>
        /// See docs/saving.md, "What ships, and where the composition asserts its own constraints".
        /// </remarks>
        [Test]
        public void RegisteringTheCurrencySchedulerOverAHoppingService_IsRefused_NamingTheKey()
        {
            ISaveService hoppingService = new SaveService(new FakeSaveCodec(), new NoProtection(), new ThreadHoppingStore(new FakeSaveStore()));
            using SaveScheduler<CurrencySaveDocument> scheduler = new(hoppingService, CurrencySaveHandler.SaveKey, new FakeGameClock());
            SaveFlushRegistry registry = new();

            SaveException error = Assert.Throws<SaveException>(() => registry.Register(scheduler));

            StringAssert.Contains($"'{CurrencySaveHandler.SaveKey}'", error.Message,
                "the refusal has to name which save was wired wrong");
            StringAssert.Contains("FlushBlocking", error.Message);
            CollectionAssert.IsEmpty(registry.Registered, "a refused scheduler must not have been registered anyway");
        }

        [Test]
        public void AddSpendAndReload_RoundTripsThroughTheRealPipeline()
        {
            ISaveService service1 = NewCurrencySaveService(_root);
            SaveScheduler<CurrencySaveDocument> scheduler1 = new(service1, CurrencySaveHandler.SaveKey, new FakeGameClock());
            CurrencyManager manager1 = new(new CurrencySaveHandler(service1, scheduler1));

            manager1.AddCurrency(CurrencyType.Coins, 100, "test");
            manager1.AddCurrency(CurrencyType.Gems, 20, "test");
            Assert.IsTrue(manager1.TrySpendCurrency(CurrencyType.Coins, 30, "test"));

            scheduler1.FlushBlocking();
            scheduler1.Dispose();

            Assert.AreEqual(70, manager1.GetCurrencyAmount(CurrencyType.Coins));
            Assert.AreEqual(20, manager1.GetCurrencyAmount(CurrencyType.Gems));

            ISaveService service2 = NewCurrencySaveService(_root);
            SaveScheduler<CurrencySaveDocument> scheduler2 = new(service2, CurrencySaveHandler.SaveKey, new FakeGameClock());
            CurrencyManager manager2 = new(new CurrencySaveHandler(service2, scheduler2));

            Assert.AreEqual(70, manager2.GetCurrencyAmount(CurrencyType.Coins));
            Assert.AreEqual(20, manager2.GetCurrencyAmount(CurrencyType.Gems));

            scheduler2.Dispose();
        }

        [Test]
        public void Load_OfASaveWrittenBeforeACurrencyExisted_KeepsItsBalances_AndStartsTheNewOneAtZero()
        {
            ISaveService service = NewCurrencySaveService(_root);
            CurrencySaveDocument olderSave = new()
            {
                ResourceAmount = new Dictionary<CurrencyType, long> { { CurrencyType.Coins, 42L } }
            };
            SynchronousUniTask.Complete(service.SaveAsync(CurrencySaveHandler.SaveKey, olderSave, CancellationToken.None));

            using SaveScheduler<CurrencySaveDocument> scheduler = new(service, CurrencySaveHandler.SaveKey, new FakeGameClock());
            CurrencyManager manager = new(new CurrencySaveHandler(service, scheduler));

            Assert.AreEqual(42, manager.GetCurrencyAmount(CurrencyType.Coins));
            Assert.AreEqual(0, manager.GetCurrencyAmount(CurrencyType.Gems));

            Assert.DoesNotThrow(() => manager.AddCurrency(CurrencyType.Gems, 3, "test"));

            Assert.AreEqual(3, manager.GetCurrencyAmount(CurrencyType.Gems));
            Assert.AreEqual(42, manager.GetCurrencyAmount(CurrencyType.Coins));
        }

        [Test]
        public void Load_OfASaveWhoseResourceAmountIsNull_StartsEveryCurrencyAtZero()
        {
            ISaveService service = NewCurrencySaveService(_root);
            CurrencySaveDocument nullSave = new() { ResourceAmount = null };
            SynchronousUniTask.Complete(service.SaveAsync(CurrencySaveHandler.SaveKey, nullSave, CancellationToken.None));

            CurrencySaveDocument stored = SynchronousUniTask.Result(
                service.LoadAsync<CurrencySaveDocument>(CurrencySaveHandler.SaveKey, CancellationToken.None));
            Assert.IsNull(stored.ResourceAmount, "guard: the null has to survive the codec, or this test never reaches the null path");

            using SaveScheduler<CurrencySaveDocument> scheduler = new(service, CurrencySaveHandler.SaveKey, new FakeGameClock());
            CurrencyManager manager = new(new CurrencySaveHandler(service, scheduler));

            Assert.AreEqual(0, manager.GetCurrencyAmount(CurrencyType.Coins));
            Assert.AreEqual(0, manager.GetCurrencyAmount(CurrencyType.Gems));

            Assert.DoesNotThrow(() => manager.AddCurrency(CurrencyType.Coins, 5, "test"));
            Assert.DoesNotThrow(() => manager.AddCurrency(CurrencyType.Gems, 3, "test"));

            Assert.AreEqual(5, manager.GetCurrencyAmount(CurrencyType.Coins));
            Assert.AreEqual(3, manager.GetCurrencyAmount(CurrencyType.Gems));
        }

        /// <remarks>
        /// See docs/saving.md, "Write coalescing, and why it cannot live inside SaveAsync".
        /// </remarks>
        [Test]
        public void ABurstOfAddsAndSpends_CoalescesWithoutChangingTheFinalBalance()
        {
            ISaveService service = NewCurrencySaveService(_root);
            SaveScheduler<CurrencySaveDocument> scheduler = new(service, CurrencySaveHandler.SaveKey, new FakeGameClock());
            CurrencyManager manager = new(new CurrencySaveHandler(service, scheduler));

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
                reloadService.LoadAsync<CurrencySaveDocument>(CurrencySaveHandler.SaveKey, CancellationToken.None));

            Assert.AreEqual(expected, reloaded.ResourceAmount[CurrencyType.Coins],
                "the one coalesced write has to carry the final balance, not any intermediate one");

            scheduler.Dispose();
        }

        /// <remarks>
        /// See docs/saving.md, "Save, then notify, for both operations".
        /// </remarks>
        [Test]
        public void AddCurrency_HandsTheNewStateToTheSaveHandler_BeforeAnyCallbackFires()
        {
            ISaveService service = NewCurrencySaveService(_root);
            using SaveScheduler<CurrencySaveDocument> scheduler = new(service, CurrencySaveHandler.SaveKey, new FakeGameClock());
            OrderRecordingHandler handler = new(new CurrencySaveHandler(service, scheduler));
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
        public void TrySpendCurrency_HandsTheNewStateToTheSaveHandler_BeforeAnyCallbackFires()
        {
            ISaveService service = NewCurrencySaveService(_root);
            using SaveScheduler<CurrencySaveDocument> scheduler = new(service, CurrencySaveHandler.SaveKey, new FakeGameClock());
            OrderRecordingHandler handler = new(new CurrencySaveHandler(service, scheduler));
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
            using SaveScheduler<CurrencySaveDocument> scheduler = new(service, CurrencySaveHandler.SaveKey, new FakeGameClock());
            OrderRecordingHandler handler = new(new CurrencySaveHandler(service, scheduler));
            CurrencyManager manager = new(handler);

            manager.OnCurrencyCollected += (currency, amount, balance, source) =>
                throw new InvalidOperationException("a listener failing for its own reasons");

            LogAssert.Expect(LogType.Exception, new Regex("a listener failing for its own reasons"));

            manager.AddCurrency(CurrencyType.Coins, 25, "throwing-listener");

            Assert.AreEqual(25, manager.GetCurrencyAmount(CurrencyType.Coins), "guard: the bank itself did take the coins");
            CollectionAssert.AreEqual(new[] { 25L }, handler.SavedCoinBalances,
                "a balance the bank already holds - and already announced - has to have reached the save handler, whatever the listener did");
            Assert.IsTrue(scheduler.HasPendingWrite, "and the handler has to have handed it on to be written");

            scheduler.FlushBlocking();
            CurrencySaveDocument reloaded = SynchronousUniTask.Result(
                NewCurrencySaveService(_root).LoadAsync<CurrencySaveDocument>(CurrencySaveHandler.SaveKey, CancellationToken.None));
            Assert.AreEqual(25, reloaded.ResourceAmount[CurrencyType.Coins], "the next session has to see the coins the player was given");
        }

        /// <remarks>
        /// See docs/saving.md, "Save, then notify, for both operations".
        /// </remarks>
        [TestCase("Collected")]
        [TestCase("Changed")]
        public void AListenerOfAnAdd_CanFlushTheAddItWasToldAbout(string eventName)
        {
            ISaveService service = NewCurrencySaveService(_root);
            using SaveScheduler<CurrencySaveDocument> scheduler = new(service, CurrencySaveHandler.SaveKey, new FakeGameClock());
            CurrencyManager manager = new(new CurrencySaveHandler(service, scheduler));
            SubscribeTo(manager, eventName, (currency, amount, balance, source) => scheduler.FlushBlocking());

            manager.AddCurrency(CurrencyType.Coins, 5, "flush-test");

            Assert.AreEqual(5, ReloadedBalance(CurrencyType.Coins));
        }

        [TestCase("Spent")]
        [TestCase("Changed")]
        public void AListenerOfASpend_CanFlushTheSpendItWasToldAbout(string eventName)
        {
            ISaveService service = NewCurrencySaveService(_root);
            using SaveScheduler<CurrencySaveDocument> scheduler = new(service, CurrencySaveHandler.SaveKey, new FakeGameClock());
            CurrencyManager manager = new(new CurrencySaveHandler(service, scheduler));

            manager.AddCurrency(CurrencyType.Coins, 10, "seed");
            scheduler.FlushBlocking();
            SubscribeTo(manager, eventName, (currency, amount, balance, source) => scheduler.FlushBlocking());

            bool spent = manager.TrySpendCurrency(CurrencyType.Coins, 4, "flush-test");

            Assert.IsTrue(spent);
            Assert.AreEqual(6, ReloadedBalance(CurrencyType.Coins));
        }

        /// <remarks>
        /// See docs/saving.md, "Save, then notify, for both operations".
        /// </remarks>
        [Test]
        public void AnAddWhoseChangedListenerAlwaysThrows_IsStillWrittenByTheBlockingFlush()
        {
            ISaveService service = NewCurrencySaveService(_root);
            using SaveScheduler<CurrencySaveDocument> scheduler = new(service, CurrencySaveHandler.SaveKey, new FakeGameClock());
            CurrencyManager manager = new(new CurrencySaveHandler(service, scheduler));
            manager.OnCurrencyChanged += (currency, amount, balance, source) => throw new InvalidOperationException("label broken");

            for (int i = 0; i < 3; i++) LogAssert.Expect(LogType.Exception, new Regex("label broken"));

            Assert.DoesNotThrow(() =>
            {
                for (int i = 0; i < 3; i++) manager.AddCurrency(CurrencyType.Coins, 5, "label-test");
            });
            scheduler.FlushBlocking();

            Assert.AreEqual(15, ReloadedBalance(CurrencyType.Coins));
        }

        [Test]
        public void AddCurrency_AfterTheSchedulerIsDisposed_ThrowsSaveException_AndChangesNothing()
        {
            ISaveService service = NewCurrencySaveService(_root);
            SaveScheduler<CurrencySaveDocument> scheduler = new(service, CurrencySaveHandler.SaveKey, new FakeGameClock());
            CurrencyManager manager = new(new CurrencySaveHandler(service, scheduler));
            List<string> events = RecordEvents(manager);

            scheduler.Dispose();

            SaveException error = Assert.Throws<SaveException>(() => manager.AddCurrency(CurrencyType.Coins, 5, "late"));

            StringAssert.Contains("disposed", error.Message);
            Assert.AreEqual(0, manager.GetCurrencyAmount(CurrencyType.Coins));
            CollectionAssert.IsEmpty(events);
        }

        [Test]
        public void TrySpendCurrency_AfterTheSchedulerIsDisposed_ThrowsSaveException_AndChangesNothing()
        {
            ISaveService service = NewCurrencySaveService(_root);
            CurrencySaveDocument seeded = new()
            {
                ResourceAmount = new Dictionary<CurrencyType, long> { { CurrencyType.Coins, 10L } }
            };
            SynchronousUniTask.Complete(service.SaveAsync(CurrencySaveHandler.SaveKey, seeded, CancellationToken.None));

            SaveScheduler<CurrencySaveDocument> scheduler = new(service, CurrencySaveHandler.SaveKey, new FakeGameClock());
            CurrencyManager manager = new(new CurrencySaveHandler(service, scheduler));
            Assert.AreEqual(10, manager.GetCurrencyAmount(CurrencyType.Coins), "guard: the seeded balance has to reach the manager");
            List<string> events = RecordEvents(manager);

            scheduler.Dispose();

            SaveException error = Assert.Throws<SaveException>(() => manager.TrySpendCurrency(CurrencyType.Coins, 4, "late"));

            StringAssert.Contains("disposed", error.Message);
            Assert.AreEqual(10, manager.GetCurrencyAmount(CurrencyType.Coins), "a retry must not be able to deduct twice");
            CollectionAssert.IsEmpty(events);
        }
    }
}
