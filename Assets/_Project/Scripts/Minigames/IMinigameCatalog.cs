using System;
using System.Collections.Generic;
using Company.ChestGame.Minigame.Core;

namespace Company.ChestGame.Minigame
{
    // The set of minigames the game knows how to build, keyed by container type. Kept separate from
    // how that set is stored so MinigameManager depends on the catalog rather than on a
    // ScriptableObject sitting at a particular Resources path.
    public interface IMinigameCatalog
    {
        IReadOnlyDictionary<Type, MinigameBaseSO> Minigames { get; }
    }
}
