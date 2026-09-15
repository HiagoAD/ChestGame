using System;
using System.Collections.Generic;
using UnityEngine;

namespace Company.ChestGame.Popups.Internal
{
    /// <summary>
    /// Pure authoring data: the popup list as the inspector holds it, holes and all.
    /// </summary>
    /// <remarks>
    /// See docs/architecture.md, "Catalogs".
    /// </remarks>
    [CreateAssetMenu(menuName = "Popups/PopupList")]
    public class PopupListSO : ScriptableObject
    {
        [SerializeField] private List<PopupBase> popups;

        public IReadOnlyList<PopupBase> Entries => popups;

        /// <remarks>
        /// See docs/architecture.md, "Catalogs".
        /// </remarks>
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
