using System;
using Company.ChestGame.Common;

namespace Company.ChestGame.Config
{
    // The config document was absent, unparseable, or carried values the game cannot run with.
    public class GameConfigException : ChestGameException
    {
        public GameConfigException(string message) : base(message) { }

        public GameConfigException(string message, Exception innerException) : base(message, innerException) { }
    }
}
