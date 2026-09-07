using System.Collections.Generic;

namespace Company.ChestGame.Minigame.Chests
{
    // Exactly two members, and no more are ever added - see docs/design-decisions.md #9. Attempts
    // is always OpenedChestIndices.Count, never a field of its own: CheckEndGame ends the run in the
    // same call that finds the prize, so a mid-run save can only ever name Open_Empty chests, and
    // nothing here can name the prize chest or reproduce the draw that would find it. ChestCount
    // exists only to detect a config change between sessions.
    public class ChestsRunSaveDocument
    {
        public const string SaveKey = "chests";

        public int ChestCount { get; set; }
        public List<int> OpenedChestIndices { get; set; } = new();
    }
}
