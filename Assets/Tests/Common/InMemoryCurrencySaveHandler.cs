using System.Collections.Generic;
using Company.ChestGame.Currency;

namespace Company.ChestGame.Tests.Common
{
    /// <summary>
    /// Keeps the saved document in a field instead of PlayerPrefs, so tests neither read nor clobber
    /// the real editor save. Sharing one instance across two CurrencyManagers exercises persistence.
    /// </summary>
    /// <remarks>
    /// Stored holds the exact document it was handed and Load hands it back, copying nothing.
    /// See docs/testing.md, "The in-memory currency save handler".
    /// </remarks>
    public class InMemoryCurrencySaveHandler : ICurrencySaveHandler
    {
        /// <summary>
        /// The exact document the last Save was handed, not a copy.
        /// </summary>
        public CurrencySaveDocument Stored { get; private set; }

        /// <summary>
        /// A copy of the balances, taken as Save ran. Null when the document or its ResourceAmount
        /// was null.
        /// </summary>
        public IReadOnlyDictionary<CurrencyType, long> LastSavedBalances { get; private set; }

        /// <summary>
        /// How many times Save has been called.
        /// </summary>
        public int SaveCallCount { get; private set; }

        /// <summary>
        /// How many times Load has been called.
        /// </summary>
        public int LoadCallCount { get; private set; }

        public void Save(CurrencySaveDocument document)
        {
            SaveCallCount++;
            Stored = document;
            LastSavedBalances = document?.ResourceAmount == null
                ? null
                : new Dictionary<CurrencyType, long>(document.ResourceAmount);
        }

        public CurrencySaveDocument Load()
        {
            LoadCallCount++;
            return Stored;
        }
    }
}
