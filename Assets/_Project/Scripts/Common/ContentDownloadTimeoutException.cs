using System;

namespace Company.ChestGame.Common
{
    // Content the game went to the network for did not arrive inside the time it was willing to
    // wait. Deliberately distinct from AssetLoadException, which means a request actually failed:
    // a stalled download never fails at all, and "it broke" and "it never answered" reach the
    // player from opposite directions — one as an error, the other as a button that stays dead for
    // the rest of the session unless something puts a deadline on the wait.
    //
    // Under ChestGameException because the player has to be told: this is the one delivery failure
    // that is otherwise completely silent.
    public class ContentDownloadTimeoutException : ChestGameException
    {
        // The label is the unit the whole delivery story works in, so it is what identifies which
        // fetch gave up, the same way MissingAssetException carries the key it could not find.
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
