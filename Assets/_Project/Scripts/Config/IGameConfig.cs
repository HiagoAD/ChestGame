using System;


namespace Company.ChestGame.Config
{
    // The game-wide config: values every part of the game may need. Anything only one minigame
    // cares about belongs to that minigame's own document, not here.
    public interface IGameConfig
    {
        public long GemsReward { get; }
        public long CoinsReward { get; }
    }
}
