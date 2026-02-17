using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Company.ChestGame.Minigame.Core
{
    [CreateAssetMenu(menuName = "Minigame/Minigame List")]
    public class MinigameListSO : ScriptableObject
    {
        [SerializeField] private List<MinigameBaseSO> minigames;

        public Dictionary<Type, MinigameBaseSO> Minigames => minigames.ToDictionary(p => p.ContainerType);

        private void OnValidate()
        {
            HashSet<Type> types = new();
            for (int i = 0; i < minigames.Count; i++)
            {
                MinigameBaseSO minigame = minigames[i];
                if(minigame == null) continue;
                if (types.Contains(minigame.ContainerType))
                {
                    minigames[i] = null;
                    throw new Exception($"INVALID ENTRY: Element at {i}, type already present");       
                }
                else
                {
                    types.Add(minigame.ContainerType);
                }
            }
        }
    }
}
