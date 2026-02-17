using System;

namespace Company.ChestGame.Minigame.Core
{
    public abstract class MinigameControllerBase : IDisposable
    {
        public abstract void NewGame();
        public abstract void Dispose();
    }
}