using Company.ChestGame.Config;

namespace Company.ChestGame.Tests.Common
{
    // Settable stand-in for IGameConfig. Defaults mirror Assets/_Project/Resources/GameConfig.json
    // so a test that only cares about one value can override just that one.
    public class FakeGameConfig : IGameConfig
    {
        public long GemsReward { get; set; } = 10;
        public long CoinsReward { get; set; } = 50;
    }
}
