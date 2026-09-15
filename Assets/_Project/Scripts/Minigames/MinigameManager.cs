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

        public MinigameContainer Get(string id)
        {
            if (id == null || !_minigameDefsById.TryGetValue(id, out MinigameBaseSO minigameSO))
            {
                throw new MinigameNotFoundException(id);
            }

            return Build(minigameSO);
        }

        /// <remarks>
        /// See docs/minigames.md, "Nothing loads while the container is built".
        /// </remarks>
        private MinigameContainer Build(MinigameBaseSO minigameSO)
        {
            MinigameContainer wrapper = minigameSO.GetMinigameContainer();
            _resolver.Inject(wrapper);

            return wrapper;
        }
    }
}
