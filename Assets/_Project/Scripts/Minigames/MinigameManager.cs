using System;
using System.Collections.Generic;
using Company.ChestGame.Minigame.Core;
using UnityEngine;
using VContainer;


namespace Company.ChestGame.Minigame
{
    public class MinigameManager : IMinigameManager
    {
        private readonly IReadOnlyDictionary<Type, MinigameBaseSO> _minigameDefs;

        private readonly IObjectResolver _resolver;

        public MinigameManager(IObjectResolver resolver, IMinigameCatalog catalog)
        {
            _minigameDefs = catalog.Minigames;
            _resolver = resolver;
        }

        public TMinigame Get<TMinigame>() where TMinigame : MinigameContainer
        {
            if (!_minigameDefs.TryGetValue(typeof(TMinigame), out MinigameBaseSO minigameSO))
            {
                throw new MinigameNotFoundException(typeof(TMinigame));
            }

            TMinigame wrapper = minigameSO.GetMinigameContainer() as TMinigame;
            _resolver.Inject(wrapper);
            _resolver.Inject(wrapper.ControllerInstance);

            return wrapper;
        }
    }
}
