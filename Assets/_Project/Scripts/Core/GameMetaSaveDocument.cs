namespace Company.ChestGame.Core
{
    // Recorded by GameBootstrapper on every launch. Timestamps are Unix milliseconds rather than a
    // date-shaped string - not because JsonCodec is known to corrupt one (a direct round trip of a
    // date-shaped string through a plain string property was checked and does not reinterpret it;
    // see docs/saving.md), but because an integer sidesteps the question of timezone and format
    // entirely rather than resting on a codec behaviour nobody re-checks the next time it changes.
    public class GameMetaSaveDocument
    {
        public const string SaveKey = "meta";

        public int Launches { get; set; }
        public long FirstLaunchUnixMs { get; set; }
        public long LastPlayedUnixMs { get; set; }
    }
}
