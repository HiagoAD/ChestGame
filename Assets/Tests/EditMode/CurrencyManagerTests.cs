using System.Collections.Generic;
using Company.ChestGame.Currency;
using Company.ChestGame.Tests.Common;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Company.ChestGame.Tests.EditMode
{
    // Every CurrencyManager here is built on an in-memory save handler, so the real PlayerPrefs
    // save is never touched. CurrencyManager logs an error on each rejected operation, which fails
    // a test by default, hence the LogAssert.Expect calls on the negative paths.
    public class CurrencyManagerTests
    {
        private InMemoryResourceBankSaveHandler _saveHandler;
        private CurrencyManager _currency;

        private List<(CurrencyType currency, long amount, long balance, string source)> _changed;
        private List<(CurrencyType currency, long amount, long balance, string source)> _collected;
        private List<(CurrencyType currency, long amount, long balance, string source)> _spent;

        [SetUp]
        public void SetUp()
        {
            _saveHandler = new InMemoryResourceBankSaveHandler();
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
            // The asymmetry is documented on ResourceBankCallbacks: Changed always describes the
            // delta applied to the balance, while Spent describes the size of the withdrawal.
            _currency.AddCurrency(CurrencyType.Coins, 100, "test");
            _changed.Clear();
            _collected.Clear();

            _currency.TrySpendCurrency(CurrencyType.Coins, 30, "shop");

            CollectionAssert.AreEqual(new[] { (CurrencyType.Coins, 30L, 70L, "shop") }, _spent);
            CollectionAssert.AreEqual(new[] { (CurrencyType.Coins, -30L, 70L, "shop") }, _changed);
            CollectionAssert.IsEmpty(_collected);
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

        // --- Debug helper ------------------------------------------------------------------

        [Test]
        public void CheatResetCurrencyAmount_ZeroesTheBalance()
        {
            _currency.AddCurrency(CurrencyType.Coins, 500, "test");

            _currency.CHEAT_ResetCurrencyAmount(CurrencyType.Coins);

            Assert.AreEqual(0, _currency.GetCurrencyAmount(CurrencyType.Coins));
        }
    }
}
