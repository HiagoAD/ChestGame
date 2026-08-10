using Company.ChestGame.Config;

namespace Company.ChestGame.Tests.Common
{
    // Settable stand-in for IGameConfig. Defaults mirror Assets/_Project/Resources/Data.json so a
    // test that only cares about one value can override just that one.
    public class FakeGameConfig : IGameConfig
    {
        public bool Initialized { get; set; } = true;

        public int ChestCount { get; set; } = 12;
        public int AttempsCount { get; set; } = 12;
        public int TimeToOpenChestMiliseconds { get; set; } = 1000;
        public long GemsReward { get; set; } = 10;
        public long CoinsReward { get; set; } = 50;
    }
}
