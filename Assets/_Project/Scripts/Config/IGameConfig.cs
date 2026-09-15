using System;


namespace Company.ChestGame.Config
{
    /// <summary>
    /// The game-wide config: values every part of the game may need.
    /// </summary>
    /// <remarks>
    /// See docs/architecture.md, "Config pipeline".
    /// See docs/design-decisions.md, "4. Fetching split from parsing in the config".
    /// </remarks>
    public interface IGameConfig
    {
        public long GemsReward { get; }
        public long CoinsReward { get; }
    }
}
