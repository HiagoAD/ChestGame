using System.Collections.Generic;
using System.Threading;
using Company.ChestGame.Minigame.Core;
using Cysharp.Threading.Tasks;

namespace Company.ChestGame.Minigame
{
    // Where the authored minigame entries come from. MinigameCatalog takes a plain list and knows
    // nothing about loading; this is the half that does, and it is the only half a different
    // loading technology has to replace.
    public interface IMinigameListSource
    {
        UniTask<IReadOnlyList<MinigameBaseSO>> ReadAsync(CancellationToken ct);
    }
}
