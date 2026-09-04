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

        public static SaveException NoKeyPrefix() =>
            new("A PlayerPrefs store needs a key prefix to namespace its keys under, and was given none");

        public static SaveException NoProfile() =>
            new("SaveServiceFactory needs a SaveProfileSO to build a service from, and was given none or a destroyed one");

        public static SaveException NoStore() =>
            new("ThreadHoppingStore needs an ISaveStore to wrap, and was given none");

        public static SaveException NoSaveService() =>
            new("A save scheduler needs an ISaveService to save through, and was given none");

        public static SaveException NoClock() =>
            new("A save scheduler needs an IGameClock to time its coalescing window against, and was given none");

        public static SaveException CoalesceWindowNotPositive(int coalesceWindowMilliseconds) =>
            new($"A save scheduler's coalescing window has to be at least 1ms, got {coalesceWindowMilliseconds}");

        public static SaveException SchedulerDisposed(string key) =>
            new($"The save scheduler for '{key}' has been disposed and cannot accept or flush any more writes");

        // Thrown by FlushBlocking, never by FlushAsync: a write already claimed by an in-flight
        // flush, or one over a save service whose store needs to leave the calling thread to finish,
        // cannot be completed synchronously without either blocking on work that thread would then
        // have to service itself - a deadlock - or silently pretending to be synchronous while
        // actually queuing the work for later, which is exactly the weaker guarantee FlushBlocking
        // exists to not offer. See docs/saving.md, "FlushBlocking, and why it cannot deadlock".
        public static SaveException FlushWouldBlock(string key) =>
            new($"FlushBlocking on the save scheduler for '{key}' would need to leave the calling thread to finish, and blocking here would risk a deadlock; call FlushAsync instead, or build this scheduler over a save service whose store never hops off the calling thread");

        public static SaveException NoProtectorKey(string protectorId) =>
            new($"The '{protectorId}' protector needs key material to protect or unprotect a payload, and was given none");

        public static SaveException KeyEscapesRoot(string key) =>
            new($"Key '{key}' names a location outside the store's root directory, which a key must never do");

        public static SaveException InvalidKey(string key) =>
            new($"Key '{key}' contains a character that cannot appear in a file name");

        public static SaveException PayloadMissing(string key) =>
            new($"The save under '{key}' carries an envelope with no body, so there is nothing to decode");

        public static SaveException PayloadUnreadable(string key, Exception innerException) =>
            new($"The save under '{key}' could not be read back; it is missing, malformed, or was written by something this build cannot decode", innerException);

        // Distinct from PayloadUnreadable: a failed MAC or signature check means the bytes were
        // provably changed after this key's protector produced them, not merely that something
        // downstream cannot parse them. SaveService is the only thing that ever produces this,
        // catching PayloadTamperedException before it reaches the generic catch below it.
        public static SaveException PayloadTampered(string key) =>
            new($"The save under '{key}' failed an integrity check; its bytes do not match what its protector signed or encrypted");

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
