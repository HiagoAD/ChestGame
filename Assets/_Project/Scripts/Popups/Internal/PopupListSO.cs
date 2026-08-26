using System;
using System.Collections.Generic;
using UnityEngine;

namespace Company.ChestGame.Popups.Internal
{
    // Pure authoring data, holes and all. Turning it into a lookup belongs to PopupCatalog.
    [CreateAssetMenu(menuName = "Popups/PopupList")]
    public class PopupListSO : ScriptableObject
    {
        [SerializeField] private List<PopupBase> popups;

        public IReadOnlyList<PopupBase> Entries => popups;

        private void OnValidate()
        {
            HashSet<Type> types = new();
            for (int i = 0; i < popups.Count; i++)
            {
                PopupBase popup = popups[i];
                if (popup == null) continue;
                if (types.Contains(popup.GetType()))
                {
                    popups[i] = null;
                    // Reported rather than thrown: OnValidate runs during asset import and on every
                    // inspector edit, where an exception aborts the surrounding operation.
                    Debug.LogError($"INVALID ENTRY: Element at {i}, type already present", this);
                }
                else
                {
                    types.Add(popup.GetType());
                }
            }
        }
    }
}
