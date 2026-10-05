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
        /// Releases what the controller acquired, including anything its injection registered.
        /// <c>MinigameContainer</c> calls it from <c>End</c>, and from <c>BeginAsync</c> when a step
        /// fails once injection has begun, including a throw inside the resolver's <c>Inject</c>
        /// itself. It must therefore be safe on a controller that was only partly injected, and it
        /// must not throw.
        /// </summary>
        /// <remarks>See docs/minigames.md, "Failure during a start".</remarks>
        public abstract void Dispose();
    }
}
