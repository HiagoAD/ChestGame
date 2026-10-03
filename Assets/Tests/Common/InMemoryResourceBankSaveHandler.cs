using System.Collections.Generic;
using Company.ChestGame.Currency;
using TapNation.Modules.ResourceBank.Saving;

namespace Company.ChestGame.Tests.Common
{
    // Keeps the bank state in a field instead of PlayerPrefs, so tests neither read nor clobber the
    // real editor save. Sharing one instance across two CurrencyManagers exercises persistence.
    //
    // Copies on the way in and on the way out, the way a real serialising handler effectively
    // does. Holding the bank's own live state instead would let a later mutation reach "what was
    // saved" without any Save at all, and would hand a second manager the first one's dictionary,
    // so a save that ran too early, or not at all, could never show up as a stale balance.
    public class InMemoryResourceBankSaveHandler : IResourceBankSaveHandler<CurrencyType>
    {
        private ResourceBankState<CurrencyType> _stored;

        // A snapshot of what the last Save was handed, as it was at that moment. A fresh copy per
        // read, so a test inspecting it cannot change what a later Load returns either.
        public ResourceBankState<CurrencyType> Stored => Copy(_stored);

        public int SaveCallCount { get; private set; }

        public void Save(ResourceBankState<CurrencyType> data)
        {
            SaveCallCount++;
            _stored = Copy(data);
        }

        public ResourceBankState<CurrencyType> Load() => Copy(_stored);

        private static ResourceBankState<CurrencyType> Copy(ResourceBankState<CurrencyType> data) =>
            data == null
                ? null
                : new ResourceBankState<CurrencyType>(new Dictionary<CurrencyType, long>(data.ResourceAmount));
    }
}
