using Company.ChestGame.Mvc;

namespace Company.ChestGame.Minigame.Core
{
    public abstract class MinigameControllerBase : IController
    {
        /// <summary>
        /// Starts a fresh run, or resumes a stored one where the minigame supports it.
        /// </summary>
        public abstract void NewGame();

        /// <summary>
        /// Releases what the controller acquired, including anything its injection registered. Must not throw.
        /// <c>MinigameContainer</c> calls it from <c>End</c>, and from <c>BeginAsync</c> when a step after
        /// injection fails; it is never called on a controller whose injection did not complete.
        /// </summary>
        /// <remarks>See docs/minigames.md, "Failure during a start".</remarks>
        public abstract void Dispose();
    }
}
