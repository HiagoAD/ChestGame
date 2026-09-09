using Newtonsoft.Json.Linq;

namespace Company.ChestGame.Saving
{
    // One step of a migration chain. A step advances a document exactly one version and never
    // further; walking several is the caller's job.
    public interface ISaveMigration
    {
        // The version this step reads. It writes FromVersion + 1.
        int FromVersion { get; }

        // The document advanced one version. Never null.
        JObject Apply(JObject document);
    }
}
