using System;
using System.Collections.Generic;
using UnityEngine;

namespace Company.ChestGame.Minigame.Core
{
    // Pure authoring data, holes and all. Turning it into a lookup belongs to MinigameCatalog.
    [CreateAssetMenu(menuName = "Minigame/Minigame List")]
    public class MinigameListSO : ScriptableObject
    {
        [SerializeField] private List<MinigameBaseSO> minigames;

        public IReadOnlyList<MinigameBaseSO> Entries => minigames;

        private void OnValidate()
        {
            HashSet<Type> types = new();
            for (int i = 0; i < minigames.Count; i++)
            {
                MinigameBaseSO minigame = minigames[i];
                if (minigame == null) continue;
                if (types.Contains(minigame.ContainerType))
                {
                    minigames[i] = null;
                    // Reported rather than thrown: OnValidate runs during asset import and on every
                    // inspector edit, where an exception aborts the surrounding operation.
                    Debug.LogError($"INVALID ENTRY: Element at {i}, type already present", this);
                }
                else
                {
                    types.Add(minigame.ContainerType);
                }
            }
        }
    }
}
