using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;
using TapNation.Modules.ResourceBank.Internal;

namespace TapNation.Modules.ResourceBank.Examples.CurrencyManager
{
    public class CurrencyManagerExample
    {
        public event ResourceBankCallbacks<Currencies>.ResourceAmountChangedDelegate OnCurrencyChange
        {
            add => _currencyBank.Callbacks.ResourceAmountChanged += value;
            remove => _currencyBank.Callbacks.ResourceAmountChanged -= value;
        }

        public event ResourceBankCallbacks<Currencies>.ResourceAmountChangedDelegate OnCurrencyCollected
        {
            add => _currencyBank.Callbacks.ResourceCollected += value;
            remove => _currencyBank.Callbacks.ResourceCollected -= value;
        }

        public event ResourceBankCallbacks<Currencies>.ResourceAmountChangedDelegate OnCurrencySpent
        {
            add => _currencyBank.Callbacks.ResourceSpent += value;
            remove => _currencyBank.Callbacks.ResourceSpent -= value;
        }


        private readonly ResourceBank<Currencies> _currencyBank;

        public CurrencyManagerExample()
        {
            string rawJson = Resources.Load<TextAsset>("CurrencyIDMap").text;
            Dictionary<Currencies, string> currencyIdMap =
                JsonConvert.DeserializeObject<Dictionary<Currencies, string>>(rawJson);

            _currencyBank = new ResourceBank<Currencies>(currencyIdMap);
        }

        public long GetCurrencyAmount(Currencies currencyType) => _currencyBank.GetResourceAmount(currencyType);

        public void AddCurrency(Currencies currencyType, long amount, string source, string GAItemType = "")
        {
            // The currency manager will just reject if amount = 0
            // and will throw a Debug.LogError and reject if amount < 0,
            // you might want to handle any special cases, especially if you want to consider 0 as a valid case,
            // check the returns of this function for more granular information
            if (!_currencyBank.TryAddResourceAmount(currencyType, amount, source))
            {
                Debug.LogError($"Failed to add {amount} {currencyType} to the bank");
                return;
            }

            // You might want to add analytics here, example:
            // GameAnalytics.NewResourceEvent(GAResourceFlowType.Source, currencyType.ToString(), amount, GAItemType,
            //     _currencyManager.ResourceIdMap[currencyType]);
            Debug.Log($"Added {amount} {currencyType} to the bank");
        }

        // This might be used together with a debugging system, so one or all currencies can be reset for testing
        public void CHEAT_ResetCurrencyAmount(Currencies currencyType)
        {
            _currencyBank.TryToSpendResource(currencyType, _currencyBank.GetResourceAmount(currencyType), "CHEAT");
        }

        // As a suggestion, this is a good place to spawn a popup offering the player to purchase the remaining currency
        public bool TrySpendCurrency(Currencies currencyType, long amount, string source, string GAItemType = "",
            bool spawnCurrencyPurchasePopup = false)
        {
            ResourceBankError bankError = _currencyBank.TryToSpendResource(currencyType, amount, source);
            if (bankError)
            {
                if (bankError == ResourceBankError.InsufficientAmount && spawnCurrencyPurchasePopup)
                {
                    long currentAmount = _currencyBank.GetResourceAmount(currencyType);
                    long remainingAmount = amount - currentAmount;
                    
                    // Spawn a popup here, example:
                    // PopupManager.Instance.ShowPopup(PopupType.CurrencyPurchase, remainingAmount);
                }
                Debug.LogError($"Failed to spend {amount} {currencyType} from the bank");
                return false;
            }

            // You might want to add analytics here, example:
            // GameAnalytics.NewResourceEvent(GAResourceFlowType.Sink, currencyType.ToString(), amount, GAItemType, 
            //     _currencyManager.ResourceIdMap[currencyType]);
            Debug.Log($"Spend {amount} {currencyType} from the bank");
            return true;
        }
    }
}