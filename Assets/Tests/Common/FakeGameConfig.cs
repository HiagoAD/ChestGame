using Company.ChestGame.Config;

namespace Company.ChestGame.Tests.Common
{
    /// <summary>
    /// Settable stand-in for <see cref="IGameConfig"/>. Defaults mirror
    /// Assets/_Project/Content/GameConfig.json so a test that only cares about one value can override
    /// just that one.
    /// </summary>
    public class FakeGameConfig : IGameConfig
    {
        public long GemsReward { get; set; } = 10;
        public long CoinsReward { get; set; } = 50;
    }
}
