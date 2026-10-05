namespace Company.ChestGame.Currency
{
    public interface ICurrencyManager
    {
        // What the three events below promise, whichever it is:
        //
        // - By the time any listener runs, the change is already in memory (GetCurrencyAmount is
        //   current) and has been handed to the save handler. It is not yet durable: a listener
        //   that needs that can flush the currency scheduler.
        // - Collected or Spent fires before Changed, and each fires in subscription order.
        // - A listener that throws is logged with Debug.LogException and never escapes the call,
        //   so it cannot stop the other listeners, the save, or the result AddCurrency and
        //   TrySpendCurrency give their caller.
        // - A listener that starts another currency operation leaves the balance argument stale for
        //   every listener after it in this operation, the following Changed raise included, and the
        //   nested operation's own events arrive before the rest of this one's. GetCurrencyAmount is
        //   the authority.
        public event CurrencyChangedHandler OnCurrencyChanged;
        public event CurrencyChangedHandler OnCurrencyCollected;
        public event CurrencyChangedHandler OnCurrencySpent;
        public long GetCurrencyAmount(CurrencyType currencyType);
        public void AddCurrency(CurrencyType currencyType, long amount, string source, string GAItemType = "");
        public bool TrySpendCurrency(CurrencyType currencyType, long amount, string source, bool spawnCurrencyPurchasePopup = false, bool acceptZeroAmount = false);
    }
}