using UnityEngine;
using TapNation.Modules.ResourceBank;
using TapNation.Modules.ResourceBank.Internal;
using TapNation.Modules.ResourceBank.Saving;

namespace Company.ChestGame.Currency
{
    // Every currency in the game, with persistence, over the ResourceBank library. Add currencies
    // by extending CurrencyType. See docs/architecture.md for what was simplified against the
    // library's own example.
    public class CurrencyManager : ICurrencyManager
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

        public CurrencyManager(IResourceBankSaveHandler<CurrencyType> saveHandler)
        {
            _currencyBank = new ResourceBank<CurrencyType>(saveHandler);
        }

        public long GetCurrencyAmount(CurrencyType currencyType) => _currencyBank.GetResourceAmount(currencyType);

        public void AddCurrency(CurrencyType currencyType, long amount, string source, string GAItemType = "")
        {
            // The bank rejects 0 silently and logs an error on a negative. Check the return value
            // if 0 has to count as valid here.
            if (!_currencyBank.TryAddResourceAmount(currencyType, amount, source))
            {
                Debug.LogError($"Failed to add {amount} {currencyType} to the bank");
                return;
            }

            // Analytics hook, example:
            // GameAnalytics.NewResourceEvent(GAResourceFlowType.Source, currencyType.ToString(), amount, GAItemType,
            //     _currencyManager.ResourceIdMap[currencyType]);
            Debug.Log($"Added {amount} {currencyType} to the bank");
        }

        // For a debugging system: reset one or all currencies for testing.
        public void CHEAT_ResetCurrencyAmount(CurrencyType currencyType)
        {
            _currencyBank.TryToSpendResource(currencyType, _currencyBank.GetResourceAmount(currencyType), "CHEAT");
        }

        // A good place to offer the player a purchase for the remaining currency.
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

            // Analytics hook, example:
            // GameAnalytics.NewResourceEvent(GAResourceFlowType.Sink, currencyType.ToString(), amount, nameof(ConsumableAddedType.Coin),
            //     source);
            Debug.Log($"Spend {amount} {currencyType} from the bank");
            return true;
        }
    }
}