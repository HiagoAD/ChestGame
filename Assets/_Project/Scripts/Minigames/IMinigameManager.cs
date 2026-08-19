using Company.ChestGame.Minigame.Core;
namespace Company.ChestGame.Minigame
{
    public interface IMinigameManager
    {
        public TContainer Get<TContainer>() where TContainer : MinigameContainer;

        // The id-keyed way in, for a caller that must not name a minigame's type at compile time.
        // The typed overload stays for callers that already have the type in hand.
        public MinigameContainer Get(string id);
    }
}
