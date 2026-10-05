using System;

namespace Company.ChestGame.Common
{
    /// <summary>
    /// Content did not arrive inside the time the game was willing to wait.
    /// </summary>
    /// <remarks>
    /// See docs/architecture.md, "Exception hierarchy".
    /// See docs/content-delivery.md, "Timeouts".
    /// </remarks>
    public class ContentDownloadTimeoutException : ChestGameException
    {
        /// <summary>
        /// The label of the fetch that did not finish in time.
        /// </summary>
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
