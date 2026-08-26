namespace Company.ChestGame.Minigame.Core
{
    // How a minigame's content is expected to arrive, authored on the definition asset next to the
    // label naming that content. Read by MinigameContentPreloader at boot and by
    // MinigameContainer.BeginAsync when a minigame starts.
    public enum MinigameLoadPolicy
    {
        // Fetched up front, before the player can ask for it.
        Preload,

        // Fetched when the minigame is actually started.
        OnDemand
    }
}
