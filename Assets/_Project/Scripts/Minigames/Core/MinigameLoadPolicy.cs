namespace Company.ChestGame.Minigame.Core
{
    // How a minigame's content is expected to arrive. Authored on the definition asset next to the
    // label naming that content, so the answer belongs to the minigame rather than to whatever code
    // later acts on it: MinigameContentPreloader reads it at boot, MinigameContainer.BeginAsync
    // reads it when a minigame starts.
    public enum MinigameLoadPolicy
    {
        // Fetched up front, before the player can ask for it.
        Preload,

        // Fetched when the minigame is actually started.
        OnDemand
    }
}
