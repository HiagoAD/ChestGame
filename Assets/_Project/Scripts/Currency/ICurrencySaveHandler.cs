namespace Company.ChestGame.Currency
{
    // Where CurrencyManager keeps its balances between runs. Load is called once, synchronously,
    // from CurrencyManager's constructor, and null means a first run. Save is handed a document
    // nothing else holds, so an implementation may keep it as it is.
    //
    // A throw from Save means "not recorded": CurrencyManager then changes nothing, raises
    // nothing, and lets the exception through. Save must not call back into CurrencyManager: the
    // document it receives already holds the new balance, which GetCurrencyAmount does not report
    // until Save returns.
    public interface ICurrencySaveHandler
    {
        void Save(CurrencySaveDocument document);

        CurrencySaveDocument Load();
    }
}
