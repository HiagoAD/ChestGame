using Company.ChestGame.Gameplay;
using VContainer;
using VContainer.Unity;

namespace Company.ChestGame.Core
{
    /// <summary>
    /// The game scene's own scope. Registers the shell controller, which needs
    /// <see cref="Company.ChestGame.Minigame.IMinigameManager"/>, resolvable only once this scope's
    /// parent chain has loaded content.
    /// </summary>
    /// <remarks>
    /// See docs/architecture.md, "Boot".
    /// </remarks>
    public class GameSceneLifetimeScope : LifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            builder.Register<GameShellController>(Lifetime.Singleton);
        }
    }
}
