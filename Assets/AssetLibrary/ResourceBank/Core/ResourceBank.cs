using UnityEngine;
using System;
using System.Collections.Generic;
using TapNation.Modules.ResourceBank.Saving;
using TapNation.Modules.ResourceBank.Internal;
using TapNation.Modules.Utils;

namespace TapNation.Modules.ResourceBank
{
    public class ResourceBank<T> where T : struct, Enum
    {
        public BidirectionalDictionary<string, T> ResourceIdMap { get; }
        public ResourceBankCallbacks<T> Callbacks { get; } = new();

        private ResourceBankState<T> _saveData;
        private IResourceBankSaveHandler<T> _saveHandler;

        public ResourceBank(IResourceBankSaveHandler<T> saveHandler = null)
        {
            ResourceIdMap = new BidirectionalDictionary<string, T>();
            Load(saveHandler);
        }

        public ResourceBank(Dictionary<string, T> resourceIdMap, IResourceBankSaveHandler<T> saveHandler = null)
        {
            ResourceIdMap = new BidirectionalDictionary<string, T>(resourceIdMap);
            Load(saveHandler);
        }

        public ResourceBank(Dictionary<T, string> resourceIdMap, IResourceBankSaveHandler<T> saveHandler = null)
        {
            ResourceIdMap = new BidirectionalDictionary<string, T>(resourceIdMap);
            Load(saveHandler);
        }

        public ResourceBankError TryAddResourceAmount(T resourceType, long amount, string source)
        {
            ResourceBankError validationError = ValidateAmount(amount);
            if(validationError != ResourceBankError.None) return validationError;

            _saveData.ResourceAmount[resourceType] += amount;
            long balance = _saveData.ResourceAmount[resourceType];
            
            Callbacks.InvokeResourceCollected(resourceType, amount, balance, source);
            Save();
            return ResourceBankError.None;
        }

        public ResourceBankError CanSpend(T resourceType, long amount)
        {
            ResourceBankError validationError = ValidateAmount(amount);
            if (validationError != ResourceBankError.None) return validationError;
            return _saveData.ResourceAmount[resourceType] >= amount ? ResourceBankError.None : ResourceBankError.InsufficientAmount;
        }

        public ResourceBankError TryToSpendResource(T resourceType, long amount, string source, bool acceptZeroAmount = false)
        {
            ResourceBankError canSpendError = CanSpend(resourceType, amount);
            if(canSpendError != ResourceBankError.None && !(canSpendError == ResourceBankError.ZeroAmount && acceptZeroAmount)) return canSpendError;

            _saveData.ResourceAmount[resourceType] -= amount;
            Save();

            long balance = _saveData.ResourceAmount[resourceType];
            Callbacks.InvokeResourceSpent(resourceType, amount, balance, source);
            return ResourceBankError.None;
        }

        public long GetResourceAmount(T resourceType) => _saveData.ResourceAmount[resourceType];
        
        private void Save()
        {
            _saveHandler.Save(_saveData);
        }

        private void Load(IResourceBankSaveHandler<T> saveHandler)
        {
            _saveHandler = saveHandler ?? new DefaultResourceBankSaveHandle<T>();

            _saveData = _saveHandler.Load() ?? new ResourceBankState<T>();
            _saveData.CheckTypeChanges();
        }

        private static ResourceBankError ValidateAmount(long amount)
        {
            return amount switch
            {
                0 => ResourceBankError.ZeroAmount,
                < 0 => ResourceBankError.NegativeAmount,
                _ => ResourceBankError.None
            };
        }
    }
}