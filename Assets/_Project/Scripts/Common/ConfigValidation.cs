using System;

namespace Company.ChestGame.Common
{
    // Shared range-check for config documents. Each document keeps its own rules; only the throwing
    // is shared.
    public static class ConfigValidation
    {
        public static void Require(bool satisfied, string fieldName, long actualValue)
        {
            if (satisfied) return;

            throw new GameConfigException($"Game config field '{fieldName}' is out of range (got {actualValue})");
        }
    }
}
