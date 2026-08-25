using Company.ChestGame.Common;

namespace Company.ChestGame.Minigame
{
    // BeginAsync on a container that is already running: a caller bug, since the manager hands out
    // a fresh container per request. Thrown rather than skipped quietly because the quiet version
    // leaks: each start takes a ref-count on the view that the single End cannot give back.
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
