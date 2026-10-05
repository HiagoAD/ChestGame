using System;

namespace Company.ChestGame.Common
{
    /// <summary>
    /// A config document was absent, unparseable, or carried values the game cannot run with.
    /// </summary>
    /// <remarks>
    /// See docs/architecture.md, "Config pipeline".
    /// </remarks>
    public class GameConfigException : ChestGameException
    {
        public GameConfigException(string message) : base(message) { }

        public GameConfigException(string message, Exception innerException) : base(message, innerException) { }
    }
}
