using System;
using Company.ChestGame.Common;

namespace Company.ChestGame.Assets
{
    // The key was in the catalog and the load still failed: a corrupt bundle, a failed download, a
    // broken dependency. MissingAssetException is the authoring mistake; only this one is worth
    // retrying.
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
