using System;
using System.Collections.Generic;
using UnityEngine;

namespace Company.ChestGame.Popups.Internal
{
    // Pure authoring data: the list as the inspector holds it, holes and all. Turning that into a
    // usable lookup, and deciding what counts as unusable, belongs to PopupCatalog.
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
                    // inspector edit, where an exception aborts the surrounding Unity operation.
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
