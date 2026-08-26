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
        private readonly IReadOnlyDictionary<string, MinigameBaseSO> _minigameDefsById;

        private readonly IObjectResolver _resolver;

        public MinigameManager(IObjectResolver resolver, IMinigameCatalog catalog)
        {
            _minigameDefs = catalog.Minigames;
            _minigameDefsById = catalog.MinigamesById;
            _resolver = resolver;
        }

        public TMinigame Get<TMinigame>() where TMinigame : MinigameContainer
        {
            if (!_minigameDefs.TryGetValue(typeof(TMinigame), out MinigameBaseSO minigameSO))
            {
                throw new MinigameNotFoundException(typeof(TMinigame));
            }

            return Build(minigameSO) as TMinigame;
        }

        // Same construction, reached without naming a container type.
        public MinigameContainer Get(string id)
        {
            if (id == null || !_minigameDefsById.TryGetValue(id, out MinigameBaseSO minigameSO))
            {
                throw new MinigameNotFoundException(id);
            }

            return Build(minigameSO);
        }

        private MinigameContainer Build(MinigameBaseSO minigameSO)
        {
            // The container only: injecting the controller here would land before its own content
            // did. That ordering belongs to BeginAsync.
            MinigameContainer wrapper = minigameSO.GetMinigameContainer();
            _resolver.Inject(wrapper);

            return wrapper;
        }
    }
}
