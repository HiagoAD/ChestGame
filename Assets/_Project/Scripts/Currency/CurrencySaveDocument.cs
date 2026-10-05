using System.Collections.Generic;

namespace Company.ChestGame.Currency
{
    // What CurrencyManager persists: one ResourceAmount dictionary of balances. Used on both the
    // save and load side, so the JSON this assembly persists is owned by this type. The property's
    // name, type and initializer are that JSON's shape, and the legacy PlayerPrefs format shares
    // it, so changing any of them breaks every save already written.
    public class CurrencySaveDocument
    {
        public Dictionary<CurrencyType, long> ResourceAmount { get; set; } = new();

        // Copies rather than aliases: CurrencyManager keeps mutating its own balances for the rest
        // of its life, and what a handler is given must be something nothing else holds.
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
