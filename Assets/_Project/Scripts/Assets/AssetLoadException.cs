using System;
using Company.ChestGame.Common;

namespace Company.ChestGame.Assets
{
    // The key was in the catalog and the load still failed: a corrupt or missing bundle, a failed
    // download, a broken dependency. Distinct from MissingAssetException, which means the game
    // asked for something it never shipped — one is an authoring mistake, the other is a runtime
    // or delivery failure, and only the second is worth retrying.
    public class AssetLoadException : ChestGameException
    {
        public string Key { get; }

        public AssetLoadException(string key, Exception innerException)
            : base($"Loading the asset at key '{key}' failed", innerException)
        {
            Key = key;
        }
    }
}
