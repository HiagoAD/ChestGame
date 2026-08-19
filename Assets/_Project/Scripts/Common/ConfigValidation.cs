using System;

namespace Company.ChestGame.Common
{
    // Shared range-check for config documents.
    //
    // A document can parse cleanly and still describe something unplayable: a field the server
    // renamed, or one this client predates, deserializes to 0. Rejecting that at the boundary is
    // the same job whichever document it is, so the check lives in one place while each document
    // keeps its own rules.
    public static class ConfigValidation
    {
        public static void Require(bool satisfied, string fieldName, long actualValue)
        {
            if (satisfied) return;

            throw new GameConfigException($"Game config field '{fieldName}' is out of range (got {actualValue})");
        }
    }
}
