using Company.ChestGame.Minigame.Core;
namespace Company.ChestGame.Minigame
{
    public interface IMinigameManager
    {
        public TContainer Get<TContainer>() where TContainer : MinigameContainer;

        /// <summary>
        /// The id-keyed way in, for a caller that must not name a minigame's type at compile time.
        /// </summary>
        public MinigameContainer Get(string id);
    }
}
