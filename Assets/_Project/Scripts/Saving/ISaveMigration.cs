using Newtonsoft.Json.Linq;

namespace Company.ChestGame.Saving
{
    // One step of the migration chain, from FromVersion to FromVersion + 1 and never further -
    // SaveMigrator is what walks a chain of these, not something any single step does on its own.
    public interface ISaveMigration
    {
        int FromVersion { get; }

        JObject Apply(JObject document);
    }
}
