using Newtonsoft.Json.Linq;

namespace Company.ChestGame.Saving
{
    /// <summary>
    /// Reads a save written before this versioning scheme existed: a different location, a
    /// different format, no version field, and hands it over as a current document. Implement one
    /// per legacy save being retired.
    /// </summary>
    /// <remarks>
    /// The members are called in this order and only in this order: <see cref="TargetKey"/>, then
    /// <see cref="IsPresent"/>, then <see cref="Import"/>, then <see cref="Clear"/> once the
    /// imported document is durably stored. Any of the last three may be skipped, so none may
    /// depend on an earlier one having run.
    /// </remarks>
    public interface ILegacyImport
    {
        /// <summary>
        /// The save key this import's data belongs under. Nothing else is ever asked of this
        /// instance for any other key, so a fixed value is the expected implementation.
        /// </summary>
        string TargetKey { get; }

        /// <summary>
        /// Whether the legacy data is still there. Must answer false once <see cref="Clear"/> has
        /// run, or the import can happen twice.
        /// </summary>
        bool IsPresent();

        /// <summary>
        /// The legacy data reshaped into a current document. Must leave the legacy data untouched:
        /// removing it is <see cref="Clear"/>'s job, and calling this does not mean the result was
        /// stored.
        /// </summary>
        JObject Import();

        /// <summary>
        /// Removes the legacy data. Only ever called once <see cref="Import"/>'s document is durably
        /// stored, so this is safe to make destructive.
        /// </summary>
        void Clear();
    }
}
