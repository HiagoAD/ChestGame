namespace Company.ChestGame.Currency
{
    public interface ICurrencyManager
    {
        /// <summary>
        /// Raised after every successful add or spend, after <see cref="OnCurrencyCollected"/> or
        /// <see cref="OnCurrencySpent"/>. The amount is signed: negative for a spend.
        /// </summary>
        /// <remarks>
        /// What the three events promise, whichever it is:
        /// - By the time any listener runs, the change is already in memory
        /// (<see cref="GetCurrencyAmount"/> is current) and has been handed to the save handler. It
        /// is not yet durable: a listener that needs that can flush the currency scheduler.
        /// - Collected or Spent fires before Changed, and each fires in subscription order.
        /// - A listener that throws is logged with Debug.LogException and never escapes the call, so
        /// it cannot stop the other listeners, the save, or the result AddCurrency and
        /// TrySpendCurrency give their caller.
        /// - A listener that starts another currency operation leaves the balance argument stale for
        /// every listener after it in this operation, the following Changed raise included, and the
        /// nested operation's own events arrive before the rest of this one's.
        /// <see cref="GetCurrencyAmount"/> is the authority.
        /// See docs/saving.md, "Save, then notify, for both operations".
        /// </remarks>
        public event CurrencyChangedHandler OnCurrencyChanged;
        /// <summary>
        /// Raised after every successful add, with the positive amount. See
        /// <see cref="OnCurrencyChanged"/> for what all three events promise.
        /// </summary>
        public event CurrencyChangedHandler OnCurrencyCollected;
        /// <summary>
        /// Raised after every successful spend, with the positive amount. See
        /// <see cref="OnCurrencyChanged"/> for what all three events promise.
        /// </summary>
        public event CurrencyChangedHandler OnCurrencySpent;
        /// <summary>
        /// The current balance of <paramref name="currencyType"/>.
        /// </summary>
        public long GetCurrencyAmount(CurrencyType currencyType);
        /// <summary>
        /// Adds <paramref name="amount"/> to the balance. A zero or negative amount is rejected and
        /// logs an error.
        /// </summary>
        public void AddCurrency(CurrencyType currencyType, long amount, string source, string GAItemType = "");
        /// <summary>
        /// Spends <paramref name="amount"/> and returns whether it did. A zero amount is rejected
        /// unless <paramref name="acceptZeroAmount"/> is set.
        /// </summary>
        public bool TrySpendCurrency(CurrencyType currencyType, long amount, string source, bool spawnCurrencyPurchasePopup = false, bool acceptZeroAmount = false);
    }
}