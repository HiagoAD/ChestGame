using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Company.ChestGame.Popups.Internal
{
    [CreateAssetMenu(menuName = "Popups/PopupList")]
    public class PopupListSO : ScriptableObject
    {
        [SerializeField] private List<PopupBase> popups;

        public Dictionary<Type, PopupBase> Popups => popups.ToDictionary(p => p.GetType());

        private void OnValidate()
        {
            HashSet<Type> types = new();
            for (int i = 0; i < popups.Count; i++)
            {
                PopupBase popup = popups[i];
                if(popup == null) continue;
                if (types.Contains(popup.GetType()))
                {
                    popups[i] = null;
                    throw new Exception($"INVALID ENTRY: Element at {i}, type already present");       
                }
                else
                {
                    types.Add(popup.GetType());
                }
            }
        }
    }
}
