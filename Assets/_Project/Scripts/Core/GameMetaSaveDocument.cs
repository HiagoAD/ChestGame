namespace Company.ChestGame.Core
{
    /// <summary>
    /// Meta save: per-install launch history, in Unix-millisecond timestamps.
    /// </summary>
    public class GameMetaSaveDocument
    {
        public const string SaveKey = "meta";

        /// <summary>
        /// Incremented on every launch.
        /// </summary>
        public int Launches { get; set; }

        /// <summary>
        /// Set once, on the first launch, and never updated after.
        /// </summary>
        public long FirstLaunchUnixMs { get; set; }

        /// <summary>
        /// Updated on every launch.
        /// </summary>
        public long LastPlayedUnixMs { get; set; }
    }
}
