using System.Collections.Generic;
using TapNation.Modules.ResourceBank.Saving;

namespace Company.ChestGame.Currency
{
    /// <summary>
    /// The same shape <c>ResourceBankState&lt;CurrencyType&gt;</c> carries - one
    /// <see cref="ResourceAmount"/> dictionary - but with its own public parameterless constructor,
    /// which a save model needs and <c>ResourceBankState&lt;T&gt;</c> does not have. Used on both
    /// the save and load side, so the JSON this assembly persists is owned by this type rather than
    /// whatever <c>ResourceBankState&lt;T&gt;</c> happens to serialize as.
    /// </summary>
    /// <remarks>
    /// See docs/saving.md, "CurrencySaveDocument, and a new() constraint the vendored model cannot satisfy".
    /// </remarks>
    public class CurrencySaveDocument
    {
        public Dictionary<CurrencyType, long> ResourceAmount { get; set; } = new();

        /// <summary>
        /// Copies rather than aliases: ResourceBank keeps mutating that same dictionary for the
        /// rest of its life, and what <c>MarkDirty</c> is handed must be something nothing else
        /// holds.
        /// </summary>
        /// <remarks>
        /// See docs/saving.md, "CurrencySaveDocument, and a new() constraint the vendored model cannot satisfy".
        /// </remarks>
        public static CurrencySaveDocument From(ResourceBankState<CurrencyType> state) =>
            new() { ResourceAmount = new Dictionary<CurrencyType, long>(state.ResourceAmount) };
    }
}
