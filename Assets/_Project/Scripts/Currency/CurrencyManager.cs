using UnityEngine;
using TapNation.Modules.ResourceBank;
using TapNation.Modules.ResourceBank.Internal;
using TapNation.Modules.ResourceBank.Saving;

namespace Company.ChestGame.Currency
{
    /// <summary>
    /// Every currency in the game, with persistence, over the ResourceBank library. Add currencies
    /// by extending <see cref="CurrencyType"/>.
    /// </summary>
    /// <remarks>
    /// See docs/architecture.md, "Currency and rewards".
    /// </remarks>
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

        /// <param name="amount">
        /// The amount to add. The bank rejects zero silently and logs an error on a negative; check
        /// the return value of the underlying bank call if zero has to count as valid here.
        /// </param>
        public void AddCurrency(CurrencyType currencyType, long amount, string source, string GAItemType = "")
        {
            if (!_currencyBank.TryAddResourceAmount(currencyType, amount, source))
            {
                Debug.LogError($"Failed to add {amount} {currencyType} to the bank");
                return;
            }

            Debug.Log($"Added {amount} {currencyType} to the bank");
        }

        /// <summary>
        /// For a debugging system: resets one or all currencies for testing.
        /// </summary>
        /// <param name="currencyType">The currency to reset to zero; call once per currency to reset all.</param>
        public void CHEAT_ResetCurrencyAmount(CurrencyType currencyType)
        {
            _currencyBank.TryToSpendResource(currencyType, _currencyBank.GetResourceAmount(currencyType), "CHEAT");
        }

        public bool TrySpendCurrency(CurrencyType currencyType, long amount, string source, bool spawnCurrencyPurchasePopup = false, bool acceptZeroAmount = false)
        {
            ResourceBankError bankError = _currencyBank.TryToSpendResource(currencyType, amount, source, acceptZeroAmount);
            if (bankError != ResourceBankError.None)
            {
                if (bankError == ResourceBankError.InsufficientAmount && spawnCurrencyPurchasePopup)
                {
                }

                Debug.LogError($"Failed to spend {amount} {currencyType} from the bank");
                return false;
            }

            Debug.Log($"Spend {amount} {currencyType} from the bank");
            return true;
        }
    }
}