using Company.ChestGame.Currency;

namespace Company.ChestGame.Tests.Common
{
    // Keeps the saved document in a field instead of PlayerPrefs, so tests neither read nor clobber
    // the real editor save. Sharing one instance across two CurrencyManagers exercises persistence.
    //
    // Holds the exact document it was handed and hands it back, copying nothing. Copying is
    // CurrencyManager's job in both directions, and a double that copied would hide a manager that
    // stopped doing it: EverySave_HandsTheHandlerASnapshotNothingElseHolds and
    // Construction_CopiesTheLoadedDocument_RatherThanAdoptingIt rely on seeing the same instance.
    public class InMemoryCurrencySaveHandler : ICurrencySaveHandler
    {
        public CurrencySaveDocument Stored { get; private set; }

        public int SaveCallCount { get; private set; }

        public int LoadCallCount { get; private set; }

        public void Save(CurrencySaveDocument document)
        {
            SaveCallCount++;
            Stored = document;
        }

        public CurrencySaveDocument Load()
        {
            LoadCallCount++;
            return Stored;
        }
    }
}
