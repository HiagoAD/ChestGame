using System.Collections.Generic;
using Company.ChestGame.Currency;

namespace Company.ChestGame.Tests.Common
{
    // Keeps the saved document in a field instead of PlayerPrefs, so tests neither read nor clobber
    // the real editor save. Sharing one instance across two CurrencyManagers exercises persistence.
    //
    // Stored holds the exact document it was handed and Load hands it back, copying nothing.
    // Copying is CurrencyManager's job in both directions, and a double that copied would hide a
    // manager that stopped doing it: EverySave_HandsTheHandlerASnapshotNothingElseHolds and
    // Construction_CopiesTheLoadedDocument_RatherThanAdoptingIt rely on seeing the same instance.
    // LastSavedBalances is the copy, taken as Save ran, for a test that needs what was saved at that
    // moment rather than a document the manager might have changed since.
    public class InMemoryCurrencySaveHandler : ICurrencySaveHandler
    {
        public CurrencySaveDocument Stored { get; private set; }

        public IReadOnlyDictionary<CurrencyType, long> LastSavedBalances { get; private set; }

        public int SaveCallCount { get; private set; }

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
