using System;
using System.Collections.Generic;

namespace TapNation.Modules.ResourceBank.Saving
{
    public class ResourceBankState<T> where T : struct, Enum
    {
        public Dictionary<T, long> ResourceAmount { get; }

        public ResourceBankState(Dictionary<T, long> resourceAmount = null)
        {
            ResourceAmount = resourceAmount ?? new Dictionary<T, long>();
            CheckTypeChanges();
        }

        public void CheckTypeChanges()
        {
            foreach (string EName in Enum.GetNames(typeof(T)))
            {
                Enum.TryParse(EName, out T E);
                ResourceAmount.TryAdd(E, 0);
            }
        }
    }
}
