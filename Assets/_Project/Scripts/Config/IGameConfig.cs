using System;


namespace Company.ChestGame.Config
{
    public interface IGameConfig
    {
        public bool Initialized { get; }

        public int ChestCount { get; }
        public int AttempsCount { get; }
        public int TimeToOpenChestMiliseconds { get; }
        public long GemsReward { get; }
        public long CoinsReward { get; }
    }
}