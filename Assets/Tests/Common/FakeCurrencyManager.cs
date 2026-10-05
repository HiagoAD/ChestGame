using System.Collections.Generic;
using Company.ChestGame.Currency;

namespace Company.ChestGame.Tests.Common
{
    // In-memory ICurrencyManager that records calls and raises the same event shapes as the real
    // one: OnCurrencySpent reports a positive amount while OnCurrencyChanged reports a negative one.
    // It neither saves nor isolates listeners: a throwing listener escapes the call here, where the
    // real CurrencyManager logs it and carries on. Do not use it to test listener-failure
    // behaviour; build the real CurrencyManager over an InMemoryCurrencySaveHandler for that.
    public class FakeCurrencyManager : ICurrencyManager
    {
        public event CurrencyChangedHandler OnCurrencyChanged;
        public event CurrencyChangedHandler OnCurrencyCollected;
        public event CurrencyChangedHandler OnCurrencySpent;

        public readonly Dictionary<CurrencyType, long> Balances = new();
        public readonly List<(CurrencyType currency, long amount, string source)> AddCalls = new();
        public readonly List<(CurrencyType currency, long amount, string source)> SpendCalls = new();

        public long GetCurrencyAmount(CurrencyType currencyType) =>
            Balances.TryGetValue(currencyType, out long amount) ? amount : 0;

        public void AddCurrency(CurrencyType currencyType, long amount, string source, string GAItemType = "")
        {
            AddCalls.Add((currencyType, amount, source));

            if (amount <= 0) return;

            long balance = GetCurrencyAmount(currencyType) + amount;
            Balances[currencyType] = balance;

            OnCurrencyCollected?.Invoke(currencyType, amount, balance, source);
            OnCurrencyChanged?.Invoke(currencyType, amount, balance, source);
        }

        public bool TrySpendCurrency(CurrencyType currencyType, long amount, string source,
            bool spawnCurrencyPurchasePopup = false, bool acceptZeroAmount = false)
        {
            SpendCalls.Add((currencyType, amount, source));

            if (amount < 0) return false;
            if (amount == 0 && !acceptZeroAmount) return false;
            if (GetCurrencyAmount(currencyType) < amount) return false;

            long balance = GetCurrencyAmount(currencyType) - amount;
            Balances[currencyType] = balance;

            OnCurrencySpent?.Invoke(currencyType, amount, balance, source);
            OnCurrencyChanged?.Invoke(currencyType, -amount, balance, source);
            return true;
        }
    }
}
