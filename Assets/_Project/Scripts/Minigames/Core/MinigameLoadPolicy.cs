namespace Company.ChestGame.Minigame.Core
{
    /// <summary>
    /// How a minigame's content is expected to arrive, authored on the definition asset next to the
    /// label naming that content. Read by <c>MinigameContentPreloader</c> at boot and by
    /// <see cref="MinigameContainer.BeginAsync"/> when a minigame starts.
    /// </summary>
    public enum MinigameLoadPolicy
    {
        /// <summary>Fetched up front, before the player can ask for it.</summary>
        Preload,

        /// <summary>Fetched when the minigame is actually started.</summary>
        OnDemand
    }
}
