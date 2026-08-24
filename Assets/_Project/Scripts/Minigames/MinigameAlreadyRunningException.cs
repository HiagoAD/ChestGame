using Company.ChestGame.Common;

namespace Company.ChestGame.Minigame
{
    // BeginAsync was called on a container that is already running. A caller bug rather than a
    // delivery failure: the shell asks the manager for a container and starts it once, and the
    // manager hands out a fresh one per request, so reaching this means someone kept a container
    // and started it twice.
    //
    // Typed and thrown rather than skipped quietly because the quiet version leaks. Each start
    // takes a ref-count on the view, and the single End that follows can only give one back.
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
