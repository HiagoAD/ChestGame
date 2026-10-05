using System.Collections.Generic;
using Company.ChestGame.Minigame.Core;
using Company.ChestGame.Popups;
using Company.ChestGame.Popups.Internal;

namespace Company.ChestGame.Core
{
    /// <summary>
    /// Everything the game needs on hand before the services that consume it are built.
    /// </summary>
    /// <remarks>
    /// A carrier only, with no loading, parsing or validation of its own.
    /// See docs/architecture.md, "Boot".
    /// </remarks>
    public class LoadedContent
    {
        public string GameConfigDocument { get; }
        public IReadOnlyList<MinigameBaseSO> Minigames { get; }
        public IReadOnlyList<PopupBase> Popups { get; }
        public PopupParent PopupParentPrefab { get; }

        public LoadedContent(
            string gameConfigDocument,
            IReadOnlyList<MinigameBaseSO> minigames,
            IReadOnlyList<PopupBase> popups,
            PopupParent popupParentPrefab)
        {
            GameConfigDocument = gameConfigDocument;
            Minigames = minigames;
            Popups = popups;
            PopupParentPrefab = popupParentPrefab;
        }
    }
}
