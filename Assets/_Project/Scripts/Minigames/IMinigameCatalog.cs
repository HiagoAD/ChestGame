using System;
using System.Collections.Generic;
using Company.ChestGame.Minigame.Core;

namespace Company.ChestGame.Minigame
{
    // The set of minigames the game knows how to build. Kept separate from how that set is stored
    // so MinigameManager depends on the catalog rather than on a ScriptableObject sitting at a
    // particular Resources path.
    //
    // The same entries are indexed twice. The type-keyed lookup serves callers that already name a
    // container type, typically a test; the id-keyed one serves the game shell, which starts a
    // minigame without referencing the assembly that defines it.
    public interface IMinigameCatalog
    {
        IReadOnlyDictionary<Type, MinigameBaseSO> Minigames { get; }
        IReadOnlyDictionary<string, MinigameBaseSO> MinigamesById { get; }
    }
}
