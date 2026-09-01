using System;
using Company.ChestGame.Common;

namespace Company.ChestGame.Saving
{
    // Under ChestGameException, unlike PoolException: every failure here can happen to a player who
    // wired the game correctly. See docs/saving.md.
    public class SaveException : ChestGameException
    {
        public SaveException(string message) : base(message) { }

        public SaveException(string message, Exception innerException) : base(message, innerException) { }

        public static SaveException NoKey() =>
            new("A save needs a key to be written or read under, and was given none");

        public static SaveException NoRootDirectory() =>
            new("A file store needs a root directory to keep its files under, and was given none");

        public static SaveException KeyEscapesRoot(string key) =>
            new($"Key '{key}' names a location outside the store's root directory, which a key must never do");

        public static SaveException InvalidKey(string key) =>
            new($"Key '{key}' contains a character that cannot appear in a file name");

        public static SaveException PayloadMissing(string key) =>
            new($"The save under '{key}' carries an envelope with no body, so there is nothing to decode");

        public static SaveException PayloadUnreadable(string key, Exception innerException) =>
            new($"The save under '{key}' could not be read back; it is missing, malformed, or was written by something this build cannot decode", innerException);

        public static SaveException VersionTooNew(string key, int foundVersion, int currentVersion) =>
            new($"The save under '{key}' is schema version {foundVersion}, newer than the {currentVersion} this build understands, and will not be partially read");

        public static SaveException NoMigrationPath(string key, int foundVersion, int currentVersion) =>
            new($"The save under '{key}' is schema version {foundVersion}, older than the {currentVersion} this build writes, and there is no migration chain yet to bring it forward");

        public static SaveException Io(string key, Exception innerException) =>
            new($"An IO failure prevented the save under '{key}' from being written or read", innerException);

        // component is "codec" or "protector".
        public static SaveException UnexpectedComponent(string key, string component, string expectedId, string foundId) =>
            new($"The save under '{key}' names {component} '{foundId ?? "none"}', but this service is configured with '{expectedId}'");
    }
}
