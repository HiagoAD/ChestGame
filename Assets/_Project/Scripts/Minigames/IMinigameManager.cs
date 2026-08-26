using Company.ChestGame.Minigame.Core;
namespace Company.ChestGame.Minigame
{
    public interface IMinigameManager
    {
        public TContainer Get<TContainer>() where TContainer : MinigameContainer;

        // The id-keyed way in, for a caller that must not name a minigame's type at compile time.
        public MinigameContainer Get(string id);
    }
}
