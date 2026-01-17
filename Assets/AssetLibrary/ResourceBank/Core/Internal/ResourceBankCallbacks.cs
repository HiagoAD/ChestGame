using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;

namespace TapNation.Modules.ResourceBank.Internal
{
    public class ResourceBankCallbacks<T> where T : struct, Enum
    {
        public delegate void ResourceAmountChangedDelegate(T resourceType, long amount, long currentBalance, string source);

        private ResourceAmountChangedDelegate _resourceAmountChanged;
        private ResourceAmountChangedDelegate _resourceCollected;
        private ResourceAmountChangedDelegate _resourceSpent;

        /// <summary>
        /// Called right after both ResourceCollected and ResourceSpent callbacks, containing the same
        /// information, the only difference is that if the amount was spent, amount will be negative,
        /// while in ResourceSpent will be positive.
        /// </summary>
        public event ResourceAmountChangedDelegate ResourceAmountChanged
        {
            add => _resourceAmountChanged += value;
            remove => _resourceAmountChanged -= value;
        }

        /// <summary>
        /// Called only when the resource amount of a certain type increases.
        /// </summary>
        public event ResourceAmountChangedDelegate ResourceCollected
        {
            add => _resourceCollected += value;
            remove => _resourceCollected -= value;
        }

        /// <summary>
        /// Called only when the resource amount of a certain type decreases.
        /// amount will be positive and represents the amount that it was decreased
        /// </summary>
        public event ResourceAmountChangedDelegate ResourceSpent
        {
            add => _resourceSpent += value;
            remove => _resourceSpent -= value;
        }

        internal void InvokeResourceCollected(T resourceType, long amount, long currentBalance, string source)
        {
            _resourceCollected?.Invoke(resourceType, amount, currentBalance, source);
            _resourceAmountChanged?.Invoke(resourceType, amount, currentBalance, source);
        }

        internal void InvokeResourceSpent(T resourceType, long amount, long currentBalance, string source)
        {
            _resourceSpent?.Invoke(resourceType, amount, currentBalance, source);
            _resourceAmountChanged?.Invoke(resourceType, -amount, currentBalance, source);
        }
    }
}