using UnityEngine;
using TapNation.Modules.ResourceBank;
using TapNation.Modules.ResourceBank.Internal;

namespace Company.ChestGame.Currency
{
    public class CurrencyManager
    {
        public event ResourceBankCallbacks<CurrencyType>.ResourceAmountChangedDelegate OnCurrencyChanged
        {
            add => _currencyBank.Callbacks.ResourceAmountChanged += value;
            remove => _currencyBank.Callbacks.ResourceAmountChanged -= value;
        }

        public event ResourceBankCallbacks<CurrencyType>.ResourceAmountChangedDelegate OnCurrencyCollected
        {
            add => _currencyBank.Callbacks.ResourceCollected += value;
            remove => _currencyBank.Callbacks.ResourceCollected -= value;
        }

        public event ResourceBankCallbacks<CurrencyType>.ResourceAmountChangedDelegate OnCurrencySpent
        {
            add => _currencyBank.Callbacks.ResourceSpent += value;
            remove => _currencyBank.Callbacks.ResourceSpent -= value;
        }

        private readonly ResourceBank<CurrencyType> _currencyBank;

        public CurrencyManager()
        {
            _currencyBank = new ResourceBank<CurrencyType>();
        }

        public long GetCurrencyAmount(CurrencyType currencyType) => _currencyBank.GetResourceAmount(currencyType);

        public void AddCurrency(CurrencyType currencyType, long amount, string source, string GAItemType = "")
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
        public void CHEAT_ResetCurrencyAmount(CurrencyType currencyType)
        {
            _currencyBank.TryToSpendResource(currencyType, _currencyBank.GetResourceAmount(currencyType), "CHEAT");
        }

        // As a suggestion, this is a good place to spawn a popup offering the player to purchase the remaining currency
        public bool TrySpendCurrency(CurrencyType currencyType, long amount, string source, bool spawnCurrencyPurchasePopup = false, bool acceptZeroAmount = false)
        {
            ResourceBankError bankError = _currencyBank.TryToSpendResource(currencyType, amount, source, acceptZeroAmount);
            if (bankError != ResourceBankError.None)
            {
                if (bankError == ResourceBankError.InsufficientAmount && spawnCurrencyPurchasePopup)
                {
                    // TODO: Open shop to complete the resource amount
                }

                Debug.LogError($"Failed to spend {amount} {currencyType} from the bank");
                return false;
            }

            // You might want to add analytics here, example:
            // GameAnalytics.NewResourceEvent(GAResourceFlowType.Sink, currencyType.ToString(), amount, nameof(ConsumableAddedType.Coin),
            //     source);
            Debug.Log($"Spend {amount} {currencyType} from the bank");
            return true;
        }
    }
}