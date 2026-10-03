namespace Company.ChestGame.Currency
{
    // Where CurrencyManager keeps its balances between runs. Load is called once, synchronously,
    // from CurrencyManager's constructor, and null means a first run. Save is handed a document
    // nothing else holds, so an implementation may keep it as it is.
    public interface ICurrencySaveHandler
    {
        void Save(CurrencySaveDocument document);

        CurrencySaveDocument Load();
    }
}
