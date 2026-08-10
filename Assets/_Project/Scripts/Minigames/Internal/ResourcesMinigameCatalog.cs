using System.Collections.Generic;
using Company.ChestGame.Common;
using Company.ChestGame.Minigame.Core;
using UnityEngine;

namespace Company.ChestGame.Minigame.Internal
{
    // Loads the authored minigame list from a Resources folder. The only place that knows the path.
    public class ResourcesMinigameCatalog : MinigameCatalog
    {
        private const string LIST_FILE_NAME = "Minigames/MinigameList";

        public ResourcesMinigameCatalog() : base(LoadEntries()) { }

        private static IReadOnlyList<MinigameBaseSO> LoadEntries()
        {
            MinigameListSO minigameListSO = Resources.Load<MinigameListSO>(LIST_FILE_NAME);
            if (minigameListSO == null)
            {
                throw new MissingAssetException(LIST_FILE_NAME, "Minigame list");
            }

            return minigameListSO.Entries;
        }
    }
}
