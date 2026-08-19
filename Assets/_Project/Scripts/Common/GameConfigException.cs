using System;

namespace Company.ChestGame.Common
{
    // A config document was absent, unparseable, or carried values the game cannot run with.
    //
    // It lives in Common rather than in Config because config documents are no longer one
    // document: each minigame owns and validates its own, and none of them should need a
    // reference to the game-wide config assembly just to name its failure.
    public class GameConfigException : ChestGameException
    {
        public GameConfigException(string message) : base(message) { }

        public GameConfigException(string message, Exception innerException) : base(message, innerException) { }
    }
}
