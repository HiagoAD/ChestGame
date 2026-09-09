namespace Company.ChestGame.Core
{
    // Unix-millisecond timestamps. Launches counts every launch; FirstLaunchUnixMs is set once and
    // never again; LastPlayedUnixMs updates on every launch.
    public class GameMetaSaveDocument
    {
        public const string SaveKey = "meta";

        public int Launches { get; set; }
        public long FirstLaunchUnixMs { get; set; }
        public long LastPlayedUnixMs { get; set; }
    }
}
