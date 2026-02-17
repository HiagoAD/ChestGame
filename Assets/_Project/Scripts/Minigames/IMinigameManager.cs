using Company.ChestGame.Minigame.Core;
namespace Company.ChestGame.Minigame
{
    public interface IMinigameManager
    {
        public TContainer Get<TContainer>() where TContainer : MinigameContainer;
    }
}
