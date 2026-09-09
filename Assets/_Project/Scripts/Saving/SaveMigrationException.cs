using System;

namespace Company.ChestGame.Saving
{
    // A migration chain was wired or walked in a way only a developer could cause: two migrations
    // claiming the same FromVersion, a target below the version already stored, or a step handing
    // back no document at all. A stored save this build genuinely has no path forward for is
    // SaveException.NoMigrationPath instead, because that one can happen to a player who did
    // nothing wrong. See docs/saving.md for why this sits beside SaveException instead of under it.
    public class SaveMigrationException : InvalidOperationException
    {
        public SaveMigrationException(string message) : base(message) { }

        public static SaveMigrationException DuplicateFromVersion(int fromVersion) =>
            new($"Two migrations both declare FromVersion {fromVersion}; a migration chain can only have one step per version");

        public static SaveMigrationException TargetBelowStoredVersion(int storedVersion, int targetVersion) =>
            new($"Asked to migrate from version {storedVersion} down to {targetVersion}; a migration chain only ever walks forward");

        public static SaveMigrationException NullDocument() =>
            new("SaveMigrator.Migrate was handed a null document; there is nothing for any step to migrate");

        public static SaveMigrationException StepReturnedNull(int fromVersion) =>
            new($"The migration step starting at FromVersion {fromVersion} returned null instead of a document; every step must hand back a document, modified or not");
    }
}
