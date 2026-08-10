using Company.ChestGame.Currency;
using TapNation.Modules.ResourceBank.Saving;

namespace Company.ChestGame.Tests.Common
{
    // Keeps the bank state in a field instead of PlayerPrefs, so tests neither read nor clobber the
    // real editor save. Sharing one instance across two CurrencyManagers exercises persistence.
    public class InMemoryResourceBankSaveHandler : IResourceBankSaveHandler<CurrencyType>
    {
        public ResourceBankState<CurrencyType> Stored { get; private set; }

        public int SaveCallCount { get; private set; }

        public void Save(ResourceBankState<CurrencyType> data)
        {
            SaveCallCount++;
            Stored = data;
        }

        public ResourceBankState<CurrencyType> Load() => Stored;
    }
}
