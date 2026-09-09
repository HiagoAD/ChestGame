using System.Collections.Generic;
using TapNation.Modules.ResourceBank.Saving;

namespace Company.ChestGame.Currency
{
    // The same shape ResourceBankState<CurrencyType> carries - one ResourceAmount dictionary - but
    // with its own public parameterless constructor, which a save model needs and
    // ResourceBankState<T> does not have. Used on both the save and load side, so the JSON this
    // assembly persists is owned by this type rather than whatever ResourceBankState<T> happens to
    // serialize as.
    public class CurrencySaveDocument
    {
        public Dictionary<CurrencyType, long> ResourceAmount { get; set; } = new();

        // Copies rather than aliases: ResourceBank keeps mutating that same dictionary for the
        // rest of its life, and what MarkDirty is handed must be something nothing else holds.
        public static CurrencySaveDocument From(ResourceBankState<CurrencyType> state) =>
            new() { ResourceAmount = new Dictionary<CurrencyType, long>(state.ResourceAmount) };
    }
}
