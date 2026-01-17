using System.Collections.Generic;
using TapNation.Modules.ResourceBank.Examples.CurrencyManager;

namespace TapNation.Modules.ResourceBank.Examples.ShardingManager
{
    // This is an example of what a cost to unlock or level up a hero might be.
    // Both costs are dictionaries to ensure that even the case of a hero costing multiple shards or currencies,
    // is handled by the manager
    public interface IHeroCostExample
    {
        public string HeroID { get; }
        public Dictionary<Shards, long> ShardCosts { get; }
        public Dictionary<Currencies, long> CurrencyCosts { get; }
    }
}