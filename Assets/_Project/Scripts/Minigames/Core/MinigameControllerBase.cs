using Company.ChestGame.Mvc;

namespace Company.ChestGame.Minigame.Core
{
    public abstract class MinigameControllerBase : IController
    {
        public abstract void NewGame();
        public abstract void Dispose();
    }
}