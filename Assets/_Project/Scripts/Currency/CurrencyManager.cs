using System;
using System.Collections.Generic;
using Company.ChestGame.Saving;
using UnityEngine;

namespace Company.ChestGame.Currency
{
    // Every currency in the game, with persistence. Add currencies by extending CurrencyType.
    //
    // Balances live in memory, loaded once in the constructor and saved after every change.
    public class CurrencyManager : ICurrencyManager
    {
        public event CurrencyChangedHandler OnCurrencyChanged;
        public event CurrencyChangedHandler OnCurrencyCollected;
        public event CurrencyChangedHandler OnCurrencySpent;

        // Why an add or a spend was refused. Not public: callers only ever learn that it failed.
        private enum Rejection
        {
            None,
            ZeroAmount,
            NegativeAmount,
            InsufficientAmount
        }

        private readonly Dictionary<CurrencyType, long> _balances = new();
        private readonly ICurrencySaveHandler _saveHandler;

        public CurrencyManager(ICurrencySaveHandler saveHandler)
        {
            // No fallback handler, deliberately: a default that wrote to PlayerPrefs would write
            // under the very key CurrencyLegacyImport reads.
            if (saveHandler == null) throw SaveException.NoSaveHandler();

            _saveHandler = saveHandler;

            // Copied entry by entry rather than adopted: the handler may still hold the document it
            // returned, and these balances are mutated for the rest of this manager's life.
            CurrencySaveDocument loaded = _saveHandler.Load();
            if (loaded != null && loaded.ResourceAmount != null)
            {
                foreach (KeyValuePair<CurrencyType, long> entry in loaded.ResourceAmount)
                {
                    _balances[entry.Key] = entry.Value;
                }
            }

            // A save written before a currency existed does not list it.
            foreach (CurrencyType currencyType in Enum.GetValues(typeof(CurrencyType)))
            {
                _balances.TryAdd(currencyType, 0);
            }
        }

        public long GetCurrencyAmount(CurrencyType currencyType) => _balances[currencyType];

        public void AddCurrency(CurrencyType currencyType, long amount, string source, string GAItemType = "")
        {
            // Zero is rejected as well as a negative, and both log this error, so a caller whose
            // amount can legitimately be 0 has to skip the call itself.
            if (TryAddAmount(currencyType, amount, source) != Rejection.None)
            {
                Debug.LogError($"Failed to add {amount} {currencyType} to the bank");
                return;
            }

            // Analytics hook, example:
            // GameAnalytics.NewResourceEvent(GAResourceFlowType.Source, currencyType.ToString(), amount, GAItemType,
            //     source);
            Debug.Log($"Added {amount} {currencyType} to the bank");
        }

        // For a debugging system: reset one or all currencies for testing.
        public void CHEAT_ResetCurrencyAmount(CurrencyType currencyType)
        {
            // The result is ignored on purpose: a zero balance is refused here without a log.
            TrySpendAmount(currencyType, _balances[currencyType], "CHEAT", acceptZeroAmount: false);
        }

        // A good place to offer the player a purchase for the remaining currency.
        public bool TrySpendCurrency(CurrencyType currencyType, long amount, string source, bool spawnCurrencyPurchasePopup = false, bool acceptZeroAmount = false)
        {
            Rejection rejection = TrySpendAmount(currencyType, amount, source, acceptZeroAmount);
            if (rejection != Rejection.None)
            {
                if (rejection == Rejection.InsufficientAmount && spawnCurrencyPurchasePopup)
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

        // Both cores save before they raise their events, so a listener that throws cannot stop the
        // new balance being saved.
        private Rejection TryAddAmount(CurrencyType currencyType, long amount, string source)
        {
            Rejection rejection = ValidateAmount(amount);
            if (rejection != Rejection.None) return rejection;

            _balances[currencyType] += amount;
            long balance = _balances[currencyType];
            Save();

            OnCurrencyCollected?.Invoke(currencyType, amount, balance, source);
            OnCurrencyChanged?.Invoke(currencyType, amount, balance, source);
            return Rejection.None;
        }

        private Rejection TrySpendAmount(CurrencyType currencyType, long amount, string source, bool acceptZeroAmount)
        {
            Rejection rejection = CanSpend(currencyType, amount);

            // Only a zero amount can be let through, and only when the caller opted in. A spend of 0
            // is then a whole operation: it saves and raises both events, with 0.
            if (rejection != Rejection.None && !(rejection == Rejection.ZeroAmount && acceptZeroAmount)) return rejection;

            _balances[currencyType] -= amount;
            Save();

            long balance = _balances[currencyType];
            OnCurrencySpent?.Invoke(currencyType, amount, balance, source);
            OnCurrencyChanged?.Invoke(currencyType, -amount, balance, source);
            return Rejection.None;
        }

        // The amount is checked before the balance, so a zero amount is ZeroAmount whatever is held.
        private Rejection CanSpend(CurrencyType currencyType, long amount)
        {
            Rejection rejection = ValidateAmount(amount);
            if (rejection != Rejection.None) return rejection;

            return _balances[currencyType] >= amount ? Rejection.None : Rejection.InsufficientAmount;
        }

        private static Rejection ValidateAmount(long amount)
        {
            return amount switch
            {
                0 => Rejection.ZeroAmount,
                < 0 => Rejection.NegativeAmount,
                _ => Rejection.None
            };
        }

        // A fresh copy every time: the handler may keep what it is handed, and this manager keeps
        // mutating _balances.
        private void Save() => _saveHandler.Save(CurrencySaveDocument.From(_balances));
    }
}