using System;

namespace Company.ChestGame.Config.Internal
{
    [Serializable]
    public class GameConfigData
    {
        public int ChestCount;
        public int AttempsCount;
        public int TimeToOpenChestMiliseconds;
        public long GemsReward;
        public long CoinsReward;
    }
}