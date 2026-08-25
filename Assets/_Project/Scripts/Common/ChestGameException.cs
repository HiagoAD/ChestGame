using System;

namespace Company.ChestGame.Common
{
    // Base for the game's own failures, so a test asserting "this throws" cannot be satisfied by an
    // unrelated NullReferenceException from inside the call.
    public class ChestGameException : Exception
    {
        public ChestGameException(string message) : base(message) { }

        public ChestGameException(string message, Exception innerException) : base(message, innerException) { }
    }
}
