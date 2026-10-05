using System.Collections.Generic;

namespace Company.ChestGame.Minigame.Chests
{
    /// <summary>
    /// The chests minigame's save document. Carries exactly two members: <see cref="ChestCount"/>
    /// and <see cref="OpenedChestIndices"/>. There is no separate attempts field; attempts is
    /// always <see cref="OpenedChestIndices"/>.Count.
    /// </summary>
    /// <remarks>
    /// See docs/saving.md, "ChestsRunSaveDocument, and decision #9 made structural".
    /// </remarks>
    public class ChestsRunSaveDocument
    {
        public const string SaveKey = "chests";

        public int ChestCount { get; set; }
        public List<int> OpenedChestIndices { get; set; } = new();
    }
}
