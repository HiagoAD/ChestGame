using System;
using TapNation.Modules.ResourceBank.Internal;

namespace Company.ChestGame.Currency
{
    public interface ICurrencyManager
    {
        public event ResourceBankCallbacks<CurrencyType>.ResourceAmountChangedDelegate OnCurrencyChanged;
        public event ResourceBankCallbacks<CurrencyType>.ResourceAmountChangedDelegate OnCurrencyCollected;
        public event ResourceBankCallbacks<CurrencyType>.ResourceAmountChangedDelegate OnCurrencySpent;
        public long GetCurrencyAmount(CurrencyType currencyType);
        public void AddCurrency(CurrencyType currencyType, long amount, string source, string GAItemType = "");
        public bool TrySpendCurrency(CurrencyType currencyType, long amount, string source, bool spawnCurrencyPurchasePopup = false, bool acceptZeroAmount = false);
    }
}