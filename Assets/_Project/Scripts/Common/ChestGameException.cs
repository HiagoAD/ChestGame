using System;

namespace Company.ChestGame.Common
{
    /// <summary>
    /// Base type for the game's own failures.
    /// </summary>
    /// <remarks>
    /// See docs/architecture.md, "Exception hierarchy".
    /// </remarks>
    public class ChestGameException : Exception
    {
        public ChestGameException(string message) : base(message) { }

        public ChestGameException(string message, Exception innerException) : base(message, innerException) { }
    }
}
