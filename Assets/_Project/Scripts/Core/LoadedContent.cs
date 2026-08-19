using System.Collections.Generic;
using Company.ChestGame.Minigame.Core;
using Company.ChestGame.Popups;
using Company.ChestGame.Popups.Internal;

namespace Company.ChestGame.Core
{
    // Everything the game has to have in hand before the services that consume it can be built.
    //
    // It is a carrier and nothing else: no loading, no parsing, no validation. That keeps the
    // ordering guarantee structural — a service holding one of these fields cannot be constructed
    // before the content arrived, so nothing downstream needs an "is it loaded yet" guard.
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
