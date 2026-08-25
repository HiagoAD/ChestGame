using System;

namespace Company.ChestGame.Common
{
    // Content did not arrive inside the time the game was willing to wait. Distinct from
    // AssetLoadException, which means a request actually failed: a stalled download never fails at
    // all, and without a deadline it leaves a button dead for the rest of the session.
    public class ContentDownloadTimeoutException : ChestGameException
    {
        // The label identifies which fetch gave up, the way MissingAssetException carries its key.
        public string Label { get; }

        public TimeSpan Timeout { get; }

        public ContentDownloadTimeoutException(string label, TimeSpan timeout)
            : base($"Downloading the content labelled '{label}' did not finish within " +
                   $"{timeout.TotalSeconds:0.###} seconds")
        {
            Label = label;
            Timeout = timeout;
        }
    }
}
