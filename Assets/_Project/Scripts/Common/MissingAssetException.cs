namespace Company.ChestGame.Common
{
    // An asset the game expects to ship with could not be found.
    public class MissingAssetException : ChestGameException
    {
        public string AssetPath { get; }

        public MissingAssetException(string assetPath, string assetKind)
            : base($"{assetKind} not found at '{assetPath}', make sure that it exists on a Resources folder")
        {
            AssetPath = assetPath;
        }
    }
}
