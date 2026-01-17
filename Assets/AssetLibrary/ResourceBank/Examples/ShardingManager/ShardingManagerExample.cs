using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Newtonsoft.Json;
using TapNation.Modules.ResourceBank.Examples.CurrencyManager;
using TapNation.Modules.ResourceBank.Internal;

namespace TapNation.Modules.ResourceBank.Examples.ShardingManager
{
    // This class is a version of CurrencyManagerExample with some extended functionality,
    // As you may be searching for this directly, the same comments were copied here and adapted
    // so it can be used separately. For the same reason, this is not an extension of that class,
    // but you may implement it this way if it matches your project structure.

    // This class will assume that in the project, Shards and Currencies are two separated systems.
    // So this example class has a function that consumes from CurrencyManagerExample, but in your project
    // this can be simplified if you want to unify those two systems, is up to you
    public class ShardingManagerExample
    {
        public event ResourceBankCallbacks<Shards>.ResourceAmountChangedDelegate OnShardChange
        {
            add => _shardBank.Callbacks.ResourceAmountChanged += value;
            remove => _shardBank.Callbacks.ResourceAmountChanged -= value;
        }

        public event ResourceBankCallbacks<Shards>.ResourceAmountChangedDelegate OnShardCollected
        {
            add => _shardBank.Callbacks.ResourceCollected += value;
            remove => _shardBank.Callbacks.ResourceCollected -= value;
        }

        public event ResourceBankCallbacks<Shards>.ResourceAmountChangedDelegate OnShardSpent
        {
            add => _shardBank.Callbacks.ResourceSpent += value;
            remove => _shardBank.Callbacks.ResourceSpent -= value;
        }


        private readonly ResourceBank<Shards> _shardBank;
        private readonly CurrencyManagerExample _currencyManager;

        public ShardingManagerExample(CurrencyManagerExample currencyManager)
        {
            string rawJson = Resources.Load<TextAsset>("ShardIDMap").text;
            Dictionary<Shards, string> shardIdMap =
                JsonConvert.DeserializeObject<Dictionary<Shards, string>>(rawJson);

            _shardBank = new ResourceBank<Shards>(shardIdMap);

            _currencyManager = currencyManager;
        }

        public long GetShardAmount(Shards shardType) => _shardBank.GetResourceAmount(shardType);

        public void AddShard(Shards shardType, long amount, string source, string GAItemType = "")
        {
            // The shard manager will just reject if amount = 0
            // and will throw a Debug.LogError and reject if amount < 0,
            // you might want to handle any special cases, especially if you want to consider 0 as a valid case,
            // check the returns of this function for more granular information
            if (!_shardBank.TryAddResourceAmount(shardType, amount, source))
            {
                Debug.LogError($"Failed to add {amount} {shardType} to the bank");
                return;
            }

            // You might want to add analytics here, example:
            // GameAnalytics.NewResourceEvent(GAResourceFlowType.Source, shardType.ToString(), amount, GAItemType,
            //     _shardManager.ResourceIdMap[shardType]);
            Debug.Log($"Added {amount} {shardType} to the bank");
        }

        // This might be used together with a debugging system, so one or all currencies can be reset for testing
        public void CHEAT_ResetShardAmount(Shards shardType)
        {
            _shardBank.TryToSpendResource(shardType, _shardBank.GetResourceAmount(shardType), "CHEAT");
        }

        // As a suggestion, this is a good place to spawn a popup offering the player to purchase the remaining shard
        public bool TrySpendShard(Shards shardType, long amount, string source, string GAItemType = "",
            bool spawnShardPurchasePopup = false)
        {
            ResourceBankError bankError = _shardBank.TryToSpendResource(shardType, amount, source);
            if (bankError)
            {
                if (bankError == ResourceBankError.InsufficientAmount && spawnShardPurchasePopup)
                {
                    long currentAmount = _shardBank.GetResourceAmount(shardType);
                    long remainingAmount = amount - currentAmount;

                    // Spawn a popup here, example:
                    // PopupManager.Instance.ShowPopup(PopupType.ShardPurchase, remainingAmount);
                }

                Debug.LogError($"Failed to spend {amount} {shardType} from the bank");
                return false;
            }

            // You might want to add analytics here, example:
            // GameAnalytics.NewResourceEvent(GAResourceFlowType.Sink, shardType.ToString(), amount, GAItemType, 
            //     _shardManager.ResourceIdMap[shardType]);
            Debug.Log($"Spend {amount} {shardType} from the bank");
            return true;
        }

        public bool TryBuyHero(IHeroCostExample heroCost, string source)
        {
            // First, we make sure that there are enough funds to purchase the hero
            bool canBuy = heroCost.ShardCosts.Keys.Aggregate(true,
                (current, shardType) => current & _shardBank.CanSpend(shardType, heroCost.ShardCosts[shardType]));
            if (!canBuy) return false;
            
            canBuy = heroCost.CurrencyCosts.Keys.Aggregate(true,
                (current, currencyType) => current & _currencyManager.GetCurrencyAmount(currencyType) >=
                    heroCost.CurrencyCosts[currencyType]);
            if (!canBuy) return false;

            // Then, we make the purchase, doing this way we avoid spending one type of currency, to later fail,
            // and have to give it back to the player

            foreach ((Shards shardType, long amount) in heroCost.ShardCosts)
            {
                _shardBank.TryToSpendResource(shardType, amount, source);
            }

            foreach ((Currencies currencyType, long amount) in heroCost.CurrencyCosts)
            {
                _currencyManager.TrySpendCurrency(currencyType, amount, source);
            }
            
            // You might want to add analytics here, example:
            // PushEvent(new HeroBoughtEvent(heroCost.HeroID));
            
            Debug.Log($"{heroCost.HeroID} unlocked");

            return true;
        }
    }
}