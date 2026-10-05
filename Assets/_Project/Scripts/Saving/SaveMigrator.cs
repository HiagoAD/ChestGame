using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Company.ChestGame.Saving
{
    /// <summary>
    /// Walks a document forward one schema version at a time, strictly ascending, never more than
    /// one version per step. Free of every Unity type, so it stays testable without a player loop.
    /// </summary>
    /// <remarks>
    /// See docs/saving.md, "The migration chain".
    /// </remarks>
    public class SaveMigrator
    {
        private readonly Dictionary<int, ISaveMigration> _byFromVersion;

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

        /// <param name="key">Names a failure only: this class holds no save of its own.</param>
        /// <remarks>
        /// See docs/saving.md, "ISaveMigration and SaveMigrator".
        /// </remarks>
        public JObject Migrate(string key, JObject document, int fromVersion, int toVersion)
        {
            if (toVersion < fromVersion) throw SaveMigrationException.TargetBelowStoredVersion(fromVersion, toVersion);

            if (document == null) throw SaveMigrationException.NullDocument();

            int version = fromVersion;
            while (version < toVersion)
            {
                if (!_byFromVersion.TryGetValue(version, out ISaveMigration migration))
                {
                    throw SaveException.NoMigrationPath(key, version, toVersion);
                }

                document = migration.Apply(document);

                if (document == null) throw SaveMigrationException.StepReturnedNull(version);

                version++;
            }

            return document;
        }
    }
}
