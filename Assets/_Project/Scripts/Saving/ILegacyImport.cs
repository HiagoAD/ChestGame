using Newtonsoft.Json.Linq;

namespace Company.ChestGame.Saving
{
    // Reads a save written before this versioning scheme existed - a different location, a
    // different format, no version field - and hands it over as a current document. Implement one
    // per legacy save being retired.
    //
    // The members are called in this order and only in this order: TargetKey, then IsPresent, then
    // Import, then Clear once the imported document is durably stored. Any of the last three may be
    // skipped, so none may depend on an earlier one having run.
    public interface ILegacyImport
    {
        // The save key this import's data belongs under. Nothing else is ever asked of this
        // instance for any other key, so a fixed value is the expected implementation.
        string TargetKey { get; }

        // Whether the legacy data is still there. Must answer false once Clear has run, or the
        // import can happen twice.
        bool IsPresent();

        // The legacy data reshaped into a current document. Must leave the legacy data untouched:
        // removing it is Clear's job, and calling this does not mean the result was stored.
        JObject Import();

        // Removes the legacy data. Only ever called once Import's document is durably stored, so
        // this is safe to make destructive.
        void Clear();
    }
}
