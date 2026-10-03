using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Company.ChestGame.Currency;
using Company.ChestGame.Saving;
using Company.ChestGame.Tests.Common;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Company.ChestGame.Tests.EditMode
{
    // Every CurrencyManager here is built on an in-memory save handler, so the real PlayerPrefs
    // save is never touched. It logs an error on each rejected operation, which fails a test by
    // default, hence the LogAssert.Expect calls on the negative paths.
    public class CurrencyManagerTests
    {
        private InMemoryCurrencySaveHandler _saveHandler;
        private CurrencyManager _currency;

        private List<(CurrencyType currency, long amount, long balance, string source)> _changed;
        private List<(CurrencyType currency, long amount, long balance, string source)> _collected;
        private List<(CurrencyType currency, long amount, long balance, string source)> _spent;

        [SetUp]
        public void SetUp()
        {
            _saveHandler = new InMemoryCurrencySaveHandler();
            _currency = new CurrencyManager(_saveHandler);

            _changed = new List<(CurrencyType, long, long, string)>();
            _collected = new List<(CurrencyType, long, long, string)>();
            _spent = new List<(CurrencyType, long, long, string)>();

            _currency.OnCurrencyChanged += (c, a, b, s) => _changed.Add((c, a, b, s));
            _currency.OnCurrencyCollected += (c, a, b, s) => _collected.Add((c, a, b, s));
            _currency.OnCurrencySpent += (c, a, b, s) => _spent.Add((c, a, b, s));
        }

        // --- Baseline ----------------------------------------------------------------------

        [Test]
        public void FreshBank_StartsEveryCurrencyAtZero()
        {
            Assert.AreEqual(0, _currency.GetCurrencyAmount(CurrencyType.Coins));
            Assert.AreEqual(0, _currency.GetCurrencyAmount(CurrencyType.Gems));
        }

        // --- Adding ------------------------------------------------------------------------

        [Test]
        public void AddCurrency_IncreasesTheBalance()
        {
            _currency.AddCurrency(CurrencyType.Coins, 50, "test");
            _currency.AddCurrency(CurrencyType.Coins, 25, "test");

            Assert.AreEqual(75, _currency.GetCurrencyAmount(CurrencyType.Coins));
            Assert.AreEqual(0, _currency.GetCurrencyAmount(CurrencyType.Gems), "currencies are tracked independently");
        }

        [Test]
        public void AddCurrency_RaisesCollectedAndChangedWithTheNewBalance()
        {
            _currency.AddCurrency(CurrencyType.Gems, 10, "chest_reward");

            CollectionAssert.AreEqual(new[] { (CurrencyType.Gems, 10L, 10L, "chest_reward") }, _collected);
            CollectionAssert.AreEqual(new[] { (CurrencyType.Gems, 10L, 10L, "chest_reward") }, _changed);
            CollectionAssert.IsEmpty(_spent);
        }

        [Test]
        public void AddCurrency_WithNegativeAmount_IsRejected()
        {
            LogAssert.Expect(LogType.Error, "Failed to add -10 Coins to the bank");

            _currency.AddCurrency(CurrencyType.Coins, -10, "test");

            Assert.AreEqual(0, _currency.GetCurrencyAmount(CurrencyType.Coins));
            CollectionAssert.IsEmpty(_changed);
        }

        [Test]
        public void AddCurrency_WithZeroAmount_IsRejected()
        {
            LogAssert.Expect(LogType.Error, "Failed to add 0 Coins to the bank");

            _currency.AddCurrency(CurrencyType.Coins, 0, "test");

            Assert.AreEqual(0, _currency.GetCurrencyAmount(CurrencyType.Coins));
            CollectionAssert.IsEmpty(_changed);
        }

        // --- Spending ----------------------------------------------------------------------

        [Test]
        public void TrySpendCurrency_WithEnoughBalance_SucceedsAndDeducts()
        {
            _currency.AddCurrency(CurrencyType.Coins, 100, "test");

            bool spent = _currency.TrySpendCurrency(CurrencyType.Coins, 30, "shop");

            Assert.IsTrue(spent);
            Assert.AreEqual(70, _currency.GetCurrencyAmount(CurrencyType.Coins));
        }

        [Test]
        public void TrySpendCurrency_ForTheExactBalance_SucceedsAndLeavesZero()
        {
            _currency.AddCurrency(CurrencyType.Coins, 40, "test");

            bool spent = _currency.TrySpendCurrency(CurrencyType.Coins, 40, "shop");

            Assert.IsTrue(spent);
            Assert.AreEqual(0, _currency.GetCurrencyAmount(CurrencyType.Coins));
        }

        [Test]
        public void TrySpendCurrency_WithNegativeAmount_IsRejectedAndCannotInflateTheBalance()
        {
            _currency.AddCurrency(CurrencyType.Coins, 100, "test");
            LogAssert.Expect(LogType.Error, "Failed to spend -50 Coins from the bank");

            bool spent = _currency.TrySpendCurrency(CurrencyType.Coins, -50, "exploit");

            Assert.IsFalse(spent);
            Assert.AreEqual(100, _currency.GetCurrencyAmount(CurrencyType.Coins));
        }

        [Test]
        public void TrySpendCurrency_BeyondTheBalance_IsRejectedAndNeverGoesNegative()
        {
            _currency.AddCurrency(CurrencyType.Coins, 10, "test");
            LogAssert.Expect(LogType.Error, "Failed to spend 25 Coins from the bank");

            bool spent = _currency.TrySpendCurrency(CurrencyType.Coins, 25, "shop");

            Assert.IsFalse(spent);
            Assert.AreEqual(10, _currency.GetCurrencyAmount(CurrencyType.Coins));
        }

        [Test]
        public void TrySpendCurrency_OnAnEmptyBank_IsRejectedAndNeverGoesNegative()
        {
            LogAssert.Expect(LogType.Error, "Failed to spend 1 Gems from the bank");

            bool spent = _currency.TrySpendCurrency(CurrencyType.Gems, 1, "shop");

            Assert.IsFalse(spent);
            Assert.AreEqual(0, _currency.GetCurrencyAmount(CurrencyType.Gems));
        }

        [Test]
        public void TrySpendCurrency_WithZeroAmount_IsRejectedByDefault()
        {
            LogAssert.Expect(LogType.Error, "Failed to spend 0 Coins from the bank");

            bool spent = _currency.TrySpendCurrency(CurrencyType.Coins, 0, "shop");

            Assert.IsFalse(spent);
        }

        [Test]
        public void TrySpendCurrency_WithZeroAmount_SucceedsWhenTheCallerOptsIn()
        {
            bool spent = _currency.TrySpendCurrency(CurrencyType.Coins, 0, "free_item", acceptZeroAmount: true);

            Assert.IsTrue(spent);
            Assert.AreEqual(0, _currency.GetCurrencyAmount(CurrencyType.Coins));
        }

        [Test]
        public void TrySpendCurrency_WithZeroAmountWhenOptedIn_SavesOnceAndRaisesSpentAndChanged()
        {
            _currency.TrySpendCurrency(CurrencyType.Coins, 0, "free_item", acceptZeroAmount: true);

            Assert.AreEqual(1, _saveHandler.SaveCallCount);
            CollectionAssert.AreEqual(new[] { (CurrencyType.Coins, 0L, 0L, "free_item") }, _spent);
            CollectionAssert.AreEqual(new[] { (CurrencyType.Coins, 0L, 0L, "free_item") }, _changed);
            CollectionAssert.IsEmpty(_collected);
        }

        [Test]
        public void TrySpendCurrency_WithNegativeAmountEvenWhenOptedIn_IsRejectedWithoutEventsOrSave()
        {
            LogAssert.Expect(LogType.Error, "Failed to spend -5 Coins from the bank");

            bool spent = _currency.TrySpendCurrency(CurrencyType.Coins, -5, "exploit", acceptZeroAmount: true);

            Assert.IsFalse(spent);
            Assert.AreEqual(0, _currency.GetCurrencyAmount(CurrencyType.Coins));
            CollectionAssert.IsEmpty(_changed);
            CollectionAssert.IsEmpty(_collected);
            CollectionAssert.IsEmpty(_spent);
            Assert.AreEqual(0, _saveHandler.SaveCallCount);
        }

        [Test]
        public void TrySpendCurrency_BeyondTheBalanceEvenWhenOptedIn_IsRejectedWithoutEventsOrSave()
        {
            _currency.AddCurrency(CurrencyType.Coins, 10, "test");
            _changed.Clear();
            _collected.Clear();
            int savesBefore = _saveHandler.SaveCallCount;
            LogAssert.Expect(LogType.Error, "Failed to spend 25 Coins from the bank");

            bool spent = _currency.TrySpendCurrency(CurrencyType.Coins, 25, "shop", acceptZeroAmount: true);

            Assert.IsFalse(spent);
            Assert.AreEqual(10, _currency.GetCurrencyAmount(CurrencyType.Coins));
            CollectionAssert.IsEmpty(_changed);
            CollectionAssert.IsEmpty(_collected);
            CollectionAssert.IsEmpty(_spent);
            Assert.AreEqual(savesBefore, _saveHandler.SaveCallCount);
        }

        [Test]
        public void RejectedOperations_RaiseNoEvents()
        {
            LogAssert.Expect(LogType.Error, "Failed to add -1 Coins to the bank");
            LogAssert.Expect(LogType.Error, "Failed to spend 5 Coins from the bank");

            _currency.AddCurrency(CurrencyType.Coins, -1, "test");
            _currency.TrySpendCurrency(CurrencyType.Coins, 5, "test");

            CollectionAssert.IsEmpty(_changed);
            CollectionAssert.IsEmpty(_collected);
            CollectionAssert.IsEmpty(_spent);
        }

        // --- Event shape -------------------------------------------------------------------

        [Test]
        public void Spending_ReportsAPositiveAmountOnSpent_AndANegativeOneOnChanged()
        {
            // The asymmetry is deliberate: Changed always describes the delta applied to the
            // balance, Spent describes the size of the withdrawal.
            _currency.AddCurrency(CurrencyType.Coins, 100, "test");
            _changed.Clear();
            _collected.Clear();

            _currency.TrySpendCurrency(CurrencyType.Coins, 30, "shop");

            CollectionAssert.AreEqual(new[] { (CurrencyType.Coins, 30L, 70L, "shop") }, _spent);
            CollectionAssert.AreEqual(new[] { (CurrencyType.Coins, -30L, 70L, "shop") }, _changed);
            CollectionAssert.IsEmpty(_collected);
        }

        // Collected and Spent fire first, Changed right after them; a listener on both sees them in
        // that order.
        [Test]
        public void AddCurrency_RaisesCollectedBeforeChanged()
        {
            List<string> order = RecordEventOrder();

            _currency.AddCurrency(CurrencyType.Coins, 10, "test");

            CollectionAssert.AreEqual(new[] { "Collected", "Changed" }, order);
        }

        [Test]
        public void TrySpendCurrency_RaisesSpentBeforeChanged()
        {
            _currency.AddCurrency(CurrencyType.Coins, 10, "test");
            List<string> order = RecordEventOrder();

            _currency.TrySpendCurrency(CurrencyType.Coins, 4, "shop");

            CollectionAssert.AreEqual(new[] { "Spent", "Changed" }, order);
        }

        private List<string> RecordEventOrder()
        {
            List<string> order = new();

            _currency.OnCurrencyCollected += (c, a, b, s) => order.Add("Collected");
            _currency.OnCurrencyChanged += (c, a, b, s) => order.Add("Changed");
            _currency.OnCurrencySpent += (c, a, b, s) => order.Add("Spent");

            return order;
        }

        // --- Listener failures -------------------------------------------------------------
        //
        // A listener that throws is logged with Debug.LogException and cannot stop the other
        // listeners, the save, or the caller's result. Listeners below only throw or capture: every
        // assertion comes after the call, because one thrown inside a listener would be caught and
        // logged instead of failing the test.

        [TestCase(typeof(InvalidOperationException))]
        [TestCase(typeof(NullReferenceException))]
        public void AddCurrency_WhenACollectedListenerThrows_StillRaisesChanged_Saves_AndReturns(Type exceptionType)
        {
            _currency.OnCurrencyCollected += Throwing("collected listener failed", exceptionType);
            ExpectLoggedException("collected listener failed");

            Assert.DoesNotThrow(() => _currency.AddCurrency(CurrencyType.Coins, 5, "test"));

            CollectionAssert.AreEqual(new[] { (CurrencyType.Coins, 5L, 5L, "test") }, _changed);
            Assert.AreEqual(1, _saveHandler.SaveCallCount);
            Assert.AreEqual(5, _saveHandler.Stored.ResourceAmount[CurrencyType.Coins]);
            Assert.AreEqual(5, _currency.GetCurrencyAmount(CurrencyType.Coins));
        }

        [Test]
        public void AddCurrency_WhenAChangedListenerThrows_LaterChangedListenersStillRun_AndTheAddIsSaved()
        {
            List<(CurrencyType currency, long amount, long balance, string source)> later = new();
            _currency.OnCurrencyChanged += Throwing("changed listener failed");
            _currency.OnCurrencyChanged += (c, a, b, s) => later.Add((c, a, b, s));
            ExpectLoggedException("changed listener failed");

            Assert.DoesNotThrow(() => _currency.AddCurrency(CurrencyType.Coins, 5, "test"));

            CollectionAssert.AreEqual(new[] { (CurrencyType.Coins, 5L, 5L, "test") }, later);
            Assert.AreEqual(1, _saveHandler.SaveCallCount);
            Assert.AreEqual(5, _saveHandler.Stored.ResourceAmount[CurrencyType.Coins]);
        }

        [TestCase(typeof(InvalidOperationException))]
        [TestCase(typeof(NullReferenceException))]
        public void TrySpendCurrency_WhenASpentListenerThrows_ReturnsTrue_RaisesChanged_AndTheSpendIsSaved(Type exceptionType)
        {
            _currency.AddCurrency(CurrencyType.Coins, 10, "seed");
            _changed.Clear();
            _currency.OnCurrencySpent += Throwing("spent listener failed", exceptionType);
            ExpectLoggedException("spent listener failed");
            bool spent = false;

            Assert.DoesNotThrow(() => spent = _currency.TrySpendCurrency(CurrencyType.Coins, 4, "shop"));

            Assert.IsTrue(spent);
            CollectionAssert.AreEqual(new[] { (CurrencyType.Coins, -4L, 6L, "shop") }, _changed);
            Assert.AreEqual(6, _saveHandler.Stored.ResourceAmount[CurrencyType.Coins]);
        }

        [Test]
        public void TrySpendCurrency_WhenAChangedListenerThrows_ReturnsTrue()
        {
            _currency.AddCurrency(CurrencyType.Coins, 10, "seed");
            _currency.OnCurrencyChanged += Throwing("changed listener failed");
            ExpectLoggedException("changed listener failed");
            bool spent = false;

            Assert.DoesNotThrow(() => spent = _currency.TrySpendCurrency(CurrencyType.Coins, 4, "shop"));

            Assert.IsTrue(spent);
            Assert.AreEqual(6, _currency.GetCurrencyAmount(CurrencyType.Coins));
            Assert.AreEqual(6, _saveHandler.Stored.ResourceAmount[CurrencyType.Coins]);
        }

        [Test]
        public void CheatResetCurrencyAmount_WhenAListenerThrows_StillZeroesRaisesChangedAndSaves()
        {
            _currency.AddCurrency(CurrencyType.Coins, 500, "seed");
            _changed.Clear();
            _currency.OnCurrencySpent += Throwing("spent listener failed");
            ExpectLoggedException("spent listener failed");

            Assert.DoesNotThrow(() => _currency.CHEAT_ResetCurrencyAmount(CurrencyType.Coins));

            Assert.AreEqual(0, _currency.GetCurrencyAmount(CurrencyType.Coins));
            CollectionAssert.AreEqual(new[] { (CurrencyType.Coins, -500L, 0L, "CHEAT") }, _changed);
            Assert.AreEqual(0, _saveHandler.Stored.ResourceAmount[CurrencyType.Coins]);
        }

        [Test]
        public void EveryThrowingListener_IsLoggedOnce_AndEveryOtherListenerStillRuns()
        {
            int recorderRuns = 0;
            _currency.OnCurrencyCollected += Throwing("first failure");
            _currency.OnCurrencyCollected += (c, a, b, s) => recorderRuns++;
            _currency.OnCurrencyCollected += Throwing("second failure");
            ExpectLoggedException("first failure");
            ExpectLoggedException("second failure");

            Assert.DoesNotThrow(() => _currency.AddCurrency(CurrencyType.Coins, 5, "test"));

            Assert.AreEqual(1, recorderRuns);
            Assert.AreEqual(1, _changed.Count);
        }

        private static CurrencyChangedHandler Throwing(string message) =>
            Throwing(message, typeof(InvalidOperationException));

        // A NullReferenceException is the realistic failure, a broken label or a destroyed view, so
        // the isolation tests run it too: a catch narrowed to one type must not pass them.
        private static CurrencyChangedHandler Throwing(string message, Type exceptionType) =>
            (c, a, b, s) => throw (Exception)Activator.CreateInstance(exceptionType, message);

        // Debug.LogException logs "<ExceptionType>: <message>", so the message alone identifies the
        // listener whatever its type. A looser pattern could let a different failure through.
        private static void ExpectLoggedException(string message) =>
            LogAssert.Expect(LogType.Exception, new Regex(Regex.Escape(message)));

        // --- What a listener sees ----------------------------------------------------------

        [Test]
        public void AnAddsListeners_SeeTheNewBalanceAlreadySavedAndInMemory()
        {
            List<(long saved, long held)> seen = new();
            CurrencyChangedHandler capture = (c, a, b, s) => seen.Add(SavedAndHeld(CurrencyType.Coins));
            _currency.OnCurrencyCollected += capture;
            _currency.OnCurrencyChanged += capture;

            _currency.AddCurrency(CurrencyType.Coins, 5, "test");

            CollectionAssert.AreEqual(new[] { (5L, 5L), (5L, 5L) }, seen);
        }

        [Test]
        public void ASpendsListeners_SeeTheNewBalanceAlreadySavedAndInMemory()
        {
            _currency.AddCurrency(CurrencyType.Coins, 10, "seed");
            List<(long saved, long held)> seen = new();
            CurrencyChangedHandler capture = (c, a, b, s) => seen.Add(SavedAndHeld(CurrencyType.Coins));
            _currency.OnCurrencySpent += capture;
            _currency.OnCurrencyChanged += capture;

            _currency.TrySpendCurrency(CurrencyType.Coins, 4, "shop");

            CollectionAssert.AreEqual(new[] { (6L, 6L), (6L, 6L) }, seen);
        }

        // The balance a listener can observe at this moment: what the save handler held when its
        // last Save ran (-1 before any did) and what GetCurrencyAmount reports. Not Stored, which
        // is live and would show a document the manager finished after handing it over.
        private (long saved, long held) SavedAndHeld(CurrencyType currencyType)
        {
            long saved = _saveHandler.LastSavedBalances == null ? -1 : _saveHandler.LastSavedBalances[currencyType];
            return (saved, _currency.GetCurrencyAmount(currencyType));
        }

        [Test]
        public void AListenerThatAddsReentrantly_LeavesTheSumInMemoryAndInTheLastSave()
        {
            bool reentered = false;
            _currency.OnCurrencyCollected += (c, a, b, s) =>
            {
                if (reentered) return;

                reentered = true;
                _currency.AddCurrency(CurrencyType.Coins, 3, "nested");
            };

            _currency.AddCurrency(CurrencyType.Coins, 5, "test");

            Assert.AreEqual(8, _currency.GetCurrencyAmount(CurrencyType.Coins));
            Assert.AreEqual(8, _saveHandler.Stored.ResourceAmount[CurrencyType.Coins]);
        }

        // --- Persistence -------------------------------------------------------------------

        [Test]
        public void Balances_SurviveThroughTheSaveHandler()
        {
            _currency.AddCurrency(CurrencyType.Coins, 250, "test");
            _currency.AddCurrency(CurrencyType.Gems, 7, "test");

            CurrencyManager reloaded = new(_saveHandler);

            Assert.AreEqual(250, reloaded.GetCurrencyAmount(CurrencyType.Coins));
            Assert.AreEqual(7, reloaded.GetCurrencyAmount(CurrencyType.Gems));
        }

        [Test]
        public void EveryMutation_IsPersisted()
        {
            _currency.AddCurrency(CurrencyType.Coins, 100, "test");
            _currency.TrySpendCurrency(CurrencyType.Coins, 40, "shop");

            Assert.AreEqual(2, _saveHandler.SaveCallCount);
        }

        [Test]
        public void SaveHandler_IsLoadedOnceAtConstruction_AndNeverAgain()
        {
            InMemoryCurrencySaveHandler handler = new();
            Assert.AreEqual(0, handler.LoadCallCount, "guard: nothing has loaded before the manager exists");

            CurrencyManager manager = new(handler);

            Assert.AreEqual(1, handler.LoadCallCount);

            manager.AddCurrency(CurrencyType.Coins, 100, "test");
            manager.AddCurrency(CurrencyType.Gems, 5, "test");
            manager.TrySpendCurrency(CurrencyType.Coins, 30, "shop");

            Assert.AreEqual(70, manager.GetCurrencyAmount(CurrencyType.Coins));
            Assert.AreEqual(5, manager.GetCurrencyAmount(CurrencyType.Gems));
            Assert.AreEqual(1, handler.LoadCallCount);
        }

        [Test]
        public void EverySave_HandsTheHandlerASnapshotNothingElseHolds()
        {
            _currency.AddCurrency(CurrencyType.Coins, 10, "test");
            CurrencySaveDocument first = _saveHandler.Stored;

            _currency.AddCurrency(CurrencyType.Coins, 5, "test");

            Assert.AreEqual(10, first.ResourceAmount[CurrencyType.Coins], "the earlier document must not follow the balance");
            Assert.AreEqual(15, _saveHandler.Stored.ResourceAmount[CurrencyType.Coins]);
            Assert.AreNotSame(first, _saveHandler.Stored);
        }

        [Test]
        public void Construction_CopiesTheLoadedDocument_RatherThanAdoptingIt()
        {
            InMemoryCurrencySaveHandler handler = new();
            CurrencySaveDocument seeded = new() { ResourceAmount = { [CurrencyType.Coins] = 7 } };
            handler.Save(seeded);

            CurrencyManager manager = new(handler);
            Assert.AreEqual(7, manager.GetCurrencyAmount(CurrencyType.Coins), "guard: the seeded balance has to reach the manager");

            manager.AddCurrency(CurrencyType.Coins, 3, "test");

            Assert.AreEqual(10, manager.GetCurrencyAmount(CurrencyType.Coins));
            Assert.AreEqual(7, seeded.ResourceAmount[CurrencyType.Coins]);
            Assert.AreEqual(1, seeded.ResourceAmount.Count, "starting a currency the save lacks must not write into the loaded document");
        }

        [Test]
        public void Constructor_WithNoSaveHandler_ThrowsSaveException()
        {
            SaveException error = Assert.Throws<SaveException>(() => new CurrencyManager(null));

            StringAssert.Contains("needs one to load and save through", error.Message);
        }

        // --- Debug helper ------------------------------------------------------------------

        [Test]
        public void CheatResetCurrencyAmount_ZeroesTheBalance()
        {
            _currency.AddCurrency(CurrencyType.Coins, 500, "test");

            _currency.CHEAT_ResetCurrencyAmount(CurrencyType.Coins);

            Assert.AreEqual(0, _currency.GetCurrencyAmount(CurrencyType.Coins));
        }

        [Test]
        public void CheatResetCurrencyAmount_OnANonZeroBalance_RaisesSpentAndChangedAndSaves()
        {
            _currency.AddCurrency(CurrencyType.Coins, 500, "test");
            _changed.Clear();
            _collected.Clear();
            _spent.Clear();
            int savesBefore = _saveHandler.SaveCallCount;

            _currency.CHEAT_ResetCurrencyAmount(CurrencyType.Coins);

            CollectionAssert.AreEqual(new[] { (CurrencyType.Coins, 500L, 0L, "CHEAT") }, _spent);
            CollectionAssert.AreEqual(new[] { (CurrencyType.Coins, -500L, 0L, "CHEAT") }, _changed);
            CollectionAssert.IsEmpty(_collected);
            Assert.AreEqual(1, _saveHandler.SaveCallCount - savesBefore);
        }

        [Test]
        public void CheatResetCurrencyAmount_OnAZeroBalance_DoesNothing()
        {
            // No LogAssert.Expect: the cheat does not go through TrySpendCurrency, so a zero
            // balance is refused without CurrencyManager logging anything. An error here would fail
            // the test.
            _currency.CHEAT_ResetCurrencyAmount(CurrencyType.Coins);

            Assert.AreEqual(0, _currency.GetCurrencyAmount(CurrencyType.Coins));
            CollectionAssert.IsEmpty(_changed);
            CollectionAssert.IsEmpty(_collected);
            CollectionAssert.IsEmpty(_spent);
            Assert.AreEqual(0, _saveHandler.SaveCallCount);
        }
    }
}
