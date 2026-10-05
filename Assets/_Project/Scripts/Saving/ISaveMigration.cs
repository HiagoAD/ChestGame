using Newtonsoft.Json.Linq;

namespace Company.ChestGame.Saving
{
    /// <summary>
    /// One step of a migration chain. A step advances a document exactly one version and never
    /// further; walking several is the caller's job.
    /// </summary>
    public interface ISaveMigration
    {
        /// <summary>The version this step reads. It writes <c>FromVersion + 1</c>.</summary>
        int FromVersion { get; }

        /// <summary>The document advanced one version. Never null.</summary>
        JObject Apply(JObject document);
    }
}
