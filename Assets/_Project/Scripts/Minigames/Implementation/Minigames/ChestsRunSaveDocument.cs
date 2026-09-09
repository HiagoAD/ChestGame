using System.Collections.Generic;

namespace Company.ChestGame.Minigame.Chests
{
    // Exactly two members. Adding a third is a deliberate act, not a casual addition - see
    // docs/design-decisions.md #9 before doing it. Attempts is always OpenedChestIndices.Count,
    // never a field of its own. ChestCount exists only to detect a config change between sessions.
    public class ChestsRunSaveDocument
    {
        public const string SaveKey = "chests";

        public int ChestCount { get; set; }
        public List<int> OpenedChestIndices { get; set; } = new();
    }
}
