namespace Company.ChestGame.Currency
{
    public interface ICurrencyManager
    {
        public event CurrencyChangedHandler OnCurrencyChanged;
        public event CurrencyChangedHandler OnCurrencyCollected;
        public event CurrencyChangedHandler OnCurrencySpent;
        public long GetCurrencyAmount(CurrencyType currencyType);
        public void AddCurrency(CurrencyType currencyType, long amount, string source, string GAItemType = "");
        public bool TrySpendCurrency(CurrencyType currencyType, long amount, string source, bool spawnCurrencyPurchasePopup = false, bool acceptZeroAmount = false);
    }
}