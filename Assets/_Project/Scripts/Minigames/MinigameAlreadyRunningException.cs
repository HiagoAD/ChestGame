using Company.ChestGame.Common;

namespace Company.ChestGame.Minigame
{
    /// <summary>
    /// Thrown when a minigame is started while a container for it is already running. A caller
    /// bug: the manager hands out a fresh container per request.
    /// </summary>
    /// <remarks>
    /// See docs/minigames.md, "Starting twice is loud".
    /// </remarks>
    public class MinigameAlreadyRunningException : ChestGameException
    {
        public string MinigameId { get; }

        public MinigameAlreadyRunningException(string minigameId)
            : base($"Minigame '{minigameId}' is already running, so it cannot be started again " +
                   "without being ended first")
        {
            MinigameId = minigameId;
        }
    }
}
