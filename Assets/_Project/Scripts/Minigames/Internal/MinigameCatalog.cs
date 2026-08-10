using System;
using System.Collections.Generic;
using Company.ChestGame.Common;
using Company.ChestGame.Minigame.Core;

namespace Company.ChestGame.Minigame.Internal
{
    // The minigames the game can build, indexed by container type.
    public class MinigameCatalog : IMinigameCatalog
    {
        public IReadOnlyDictionary<Type, MinigameBaseSO> Minigames { get; }

        public MinigameCatalog(IReadOnlyList<MinigameBaseSO> entries) =>
            Minigames = CatalogBuilder.Build(entries, entry => entry.ContainerType, "Minigame list");
    }
}
