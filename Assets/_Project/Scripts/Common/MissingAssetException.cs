using System;

namespace Company.ChestGame.Common
{
    // An asset the game expects to ship with could not be found. The path is whatever key the
    // loader was given — an addressable address today — and Common stays free of any opinion about
    // which loader that is.
    public class MissingAssetException : ChestGameException
    {
        public string AssetPath { get; }

        public MissingAssetException(string assetPath, string assetKind)
            : base(MessageFor(assetPath, assetKind))
        {
            AssetPath = assetPath;
        }

        // The loader knows why the lookup failed, and losing that would leave a report with nothing
        // in it but the key the game asked for.
        public MissingAssetException(string assetPath, string assetKind, Exception innerException)
            : base(MessageFor(assetPath, assetKind), innerException)
        {
            AssetPath = assetPath;
        }

        private static string MessageFor(string assetPath, string assetKind) =>
            $"{assetKind} not found at '{assetPath}', make sure that it ships with the game under that key";
    }
}
