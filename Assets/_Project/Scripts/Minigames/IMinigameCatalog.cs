using System;
using System.Collections.Generic;
using Company.ChestGame.Minigame.Core;

namespace Company.ChestGame.Minigame
{
    // The set of minigames the game knows how to build, indexed twice over the same entries: by
    // container type for callers that already name one, typically a test, and by authored id for
    // the shell, which must not.
    public interface IMinigameCatalog
    {
        IReadOnlyDictionary<Type, MinigameBaseSO> Minigames { get; }
        IReadOnlyDictionary<string, MinigameBaseSO> MinigamesById { get; }
    }
}
