using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Company.ChestGame.Saving
{
    // Walks a document forward one schema version at a time, strictly ascending, never more than
    // one version per step. Free of every Unity type, so it stays testable without a player loop.
    // See docs/saving.md, "The migration chain".
    public class SaveMigrator
    {
        private readonly Dictionary<int, ISaveMigration> _byFromVersion;

        // Two migrations sharing a FromVersion is a wiring mistake, not something a player's save
        // can cause, so it throws here rather than at the first old save that needs that step.
        public SaveMigrator(IEnumerable<ISaveMigration> migrations)
        {
            _byFromVersion = new Dictionary<int, ISaveMigration>();

            foreach (ISaveMigration migration in migrations ?? Array.Empty<ISaveMigration>())
            {
                if (!_byFromVersion.TryAdd(migration.FromVersion, migration))
                {
                    throw SaveMigrationException.DuplicateFromVersion(migration.FromVersion);
                }
            }
        }

        // key exists only to name a failure: this class holds no save of its own. Reusing
        // NoMigrationPath makes a missing step read the same way a save with no chain at all does.
        public JObject Migrate(string key, JObject document, int fromVersion, int toVersion)
        {
            if (toVersion < fromVersion) throw SaveMigrationException.TargetBelowStoredVersion(fromVersion, toVersion);

            // Guarded here rather than left to surface as a NullReferenceException in the first
            // step, or worse, a step handing null back out as if it had migrated something.
            if (document == null) throw SaveMigrationException.NullDocument();

            int version = fromVersion;
            while (version < toVersion)
            {
                // Names the version the walk is actually stuck at, which is only the originally
                // stored version if the very first step is the one missing - a gap further along
                // the chain is named by where it is, not by where the walk started.
                if (!_byFromVersion.TryGetValue(version, out ISaveMigration migration))
                {
                    throw SaveException.NoMigrationPath(key, version, toVersion);
                }

                document = migration.Apply(document);

                // A wiring mistake, not something a save can cause. Caught immediately so it never
                // surfaces later as an unexplained failure several calls away from the step that
                // actually caused it.
                if (document == null) throw SaveMigrationException.StepReturnedNull(version);

                version++;
            }

            return document;
        }
    }
}
