using System.Collections.Generic;
using TapNation.Modules.ResourceBank.Saving;

namespace Company.ChestGame.Currency
{
    // What CurrencyResourceBankSaveHandle actually persists through ISaveService: the same shape
    // ResourceBankState<CurrencyType> already carries - one dictionary, ResourceAmount - but its own
    // type rather than a direct use of ResourceBankState<T> itself.
    //
    // ISaveService.LoadAsync<T> (and SaveService's own legacy-import path) both require T : class,
    // new(), and ResourceBankState<T>'s only constructor takes an optional Dictionary<T, long>
    // argument - a real CS0310 the moment it is asked to stand in for that T, because a constructor
    // with a default argument is not a parameterless constructor as far as the new() constraint is
    // concerned. Confirmed against a real compile, not assumed. ResourceBank is vendored and not to
    // be touched, so this type exists instead of a constructor change to it.
    //
    // MarkDirty/SaveAsync only require class, so the save direction could have used
    // ResourceBankState<CurrencyType> directly and only Load() needed a stand-in. Using this type on
    // both sides instead is deliberate: it keeps the JSON this assembly actually persists owned by
    // this adapter, rather than one direction of it silently tracking whatever shape a future update
    // to the vendored library's own ResourceBankState<T> happens to serialize as.
    public class CurrencySaveDocument
    {
        public Dictionary<CurrencyType, long> ResourceAmount { get; set; } = new();

        // Copies rather than aliases data.ResourceAmount: ResourceBank keeps mutating that same
        // dictionary instance for the rest of its life (every TryAddResourceAmount/
        // TryToSpendResource writes straight into it), and the state SaveScheduler<T>.MarkDirty is
        // handed is documented to be something nothing else holds a reference to once it is
        // captured. Copying here is what makes that true for this document, rather than merely
        // assumed of it.
        public static CurrencySaveDocument From(ResourceBankState<CurrencyType> state) =>
            new() { ResourceAmount = new Dictionary<CurrencyType, long>(state.ResourceAmount) };
    }
}
