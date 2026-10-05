namespace Company.ChestGame.Currency
{
    /// <summary>
    /// Where CurrencyManager keeps its balances between runs. Load is called once, synchronously,
    /// from CurrencyManager's constructor, and null means a first run. Save is handed a document
    /// nothing else holds, so an implementation may keep it as it is.
    /// </summary>
    /// <remarks>
    /// A throw from Save means "not recorded": CurrencyManager then changes nothing, raises
    /// nothing, and lets the exception through. Save must not call back into CurrencyManager: the
    /// document it receives already holds the new balance, which GetCurrencyAmount does not report
    /// until Save returns.
    /// See docs/saving.md, "Save, then notify, for both operations".
    /// </remarks>
    public interface ICurrencySaveHandler
    {
        /// <summary>
        /// Records the document. A throw means it was not recorded.
        /// </summary>
        void Save(CurrencySaveDocument document);

        /// <summary>
        /// Returns the saved document, or null on a first run.
        /// </summary>
        CurrencySaveDocument Load();
    }
}
