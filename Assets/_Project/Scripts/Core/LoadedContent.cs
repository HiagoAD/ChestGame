using System.Collections.Generic;
using Company.ChestGame.Minigame.Core;
using Company.ChestGame.Popups;
using Company.ChestGame.Popups.Internal;

namespace Company.ChestGame.Core
{
    // Everything the game has to have in hand before the services that consume it can be built. A
    // carrier and nothing else, which keeps the ordering guarantee structural: nothing downstream
    // needs an "is it loaded yet" guard.
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
