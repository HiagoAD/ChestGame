using System;
using System.Collections.Generic;
using Company.ChestGame.Minigame.Core;
using UnityEngine;
using VContainer;


namespace Company.ChestGame.Minigame
{
    public class MinigameManager : IMinigameManager
    {
        private const string LIST_FILE_NAME = "Minigames/MinigameList";
        private Dictionary<Type, MinigameBaseSO> _minigameDefs;

        private IObjectResolver _resolver;
        public MinigameManager(IObjectResolver resolver)
        {
            MinigameListSO minigameListSO = Resources.Load<MinigameListSO>(LIST_FILE_NAME);
            if (minigameListSO == null)
            {
                throw new Exception($"File {LIST_FILE_NAME} not found, make sure that it exists on a Resources folder");
            }

            _minigameDefs = minigameListSO.Minigames;

            _resolver = resolver;
        }

        public TMinigame Get<TMinigame>() where TMinigame : MinigameContainer
        {
            if (!_minigameDefs.TryGetValue(typeof(TMinigame), out MinigameBaseSO minigameSO))
            {
                throw new Exception("Minigame prefab not found");
            }

            TMinigame wrapper = minigameSO.GetMinigameContainer() as TMinigame;
            _resolver.Inject(wrapper);
            _resolver.Inject(wrapper.ControllerInstance);

            return wrapper;
        }
    }
}
