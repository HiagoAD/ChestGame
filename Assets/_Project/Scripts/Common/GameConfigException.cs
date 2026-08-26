using System;

namespace Company.ChestGame.Common
{
    // A config document was absent, unparseable, or carried values the game cannot run with. In
    // Common rather than Config because each minigame owns and validates its own document, and none
    // should need a reference to the game-wide config assembly to name its failure.
    public class GameConfigException : ChestGameException
    {
        public GameConfigException(string message) : base(message) { }

        public GameConfigException(string message, Exception innerException) : base(message, innerException) { }
    }
}
