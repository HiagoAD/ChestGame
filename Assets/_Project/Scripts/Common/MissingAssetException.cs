using System;

namespace Company.ChestGame.Common
{
    /// <summary>
    /// An asset the game expects to ship with could not be found.
    /// </summary>
    /// <remarks>
    /// See docs/architecture.md, "Exception hierarchy".
    /// </remarks>
    public class MissingAssetException : ChestGameException
    {
        public string AssetPath { get; }

        public MissingAssetException(string assetPath, string assetKind)
            : base(MessageFor(assetPath, assetKind))
        {
            AssetPath = assetPath;
        }

        public MissingAssetException(string assetPath, string assetKind, Exception innerException)
            : base(MessageFor(assetPath, assetKind), innerException)
        {
            AssetPath = assetPath;
        }

        private static string MessageFor(string assetPath, string assetKind) =>
            $"{assetKind} not found at '{assetPath}', make sure that it ships with the game under that key";
    }
}
