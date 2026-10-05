using System.Collections.Generic;

namespace Company.ChestGame.Currency
{
    /// <summary>
    /// What CurrencyManager persists: one ResourceAmount dictionary of balances. Used on both the
    /// save and load side, so the JSON this assembly persists is owned by this type.
    /// </summary>
    /// <remarks>
    /// The property's name, type and initializer are that JSON's shape, and the legacy PlayerPrefs
    /// format shares it, so changing any of them breaks every save already written.
    /// See docs/saving.md, "CurrencySaveDocument, and a new() constraint the library's model could not satisfy".
    /// </remarks>
    public class CurrencySaveDocument
    {
        /// <summary>
        /// The balance of each currency.
        /// </summary>
        public Dictionary<CurrencyType, long> ResourceAmount { get; set; } = new();

        /// <summary>
        /// Returns a document over a copy of <paramref name="balances"/>, so what a handler is given
        /// is something nothing else holds.
        /// </summary>
        public static CurrencySaveDocument From(IReadOnlyDictionary<CurrencyType, long> balances)
        {
            Dictionary<CurrencyType, long> copy = new();
            foreach (KeyValuePair<CurrencyType, long> balance in balances)
            {
                copy.Add(balance.Key, balance.Value);
            }

            return new CurrencySaveDocument { ResourceAmount = copy };
        }
    }
}
