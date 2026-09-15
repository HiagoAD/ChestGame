using System;
using System.Collections.Generic;
using UnityEngine;

namespace Company.ChestGame.Minigame.Core
{
    /// <summary>
    /// Pure authoring data, holes and all. Turning it into a lookup belongs to <c>MinigameCatalog</c>.
    /// </summary>
    [CreateAssetMenu(menuName = "Minigame/Minigame List")]
    public class MinigameListSO : ScriptableObject
    {
        [SerializeField] private List<MinigameBaseSO> minigames;

        public IReadOnlyList<MinigameBaseSO> Entries => minigames;

        /// <remarks>
        /// See docs/architecture.md, "Catalogs".
        /// </remarks>
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
