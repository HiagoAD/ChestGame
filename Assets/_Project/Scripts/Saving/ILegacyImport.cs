using Newtonsoft.Json.Linq;

namespace Company.ChestGame.Saving
{
    // A save that predates this envelope entirely: a different store, a different key, no version
    // field at all, so there is nothing for SaveMigrator to read a version from. SaveService runs
    // this before the chain rather than through it, and only when its own store has nothing under
    // the key it was asked for - see docs/saving.md, "The legacy import", for the ordering guarantee
    // that section exists to spell out.
    //
    // Generic on purpose: nothing under Company.ChestGame.Saving may know CurrencyType, ResourceBank
    // or any other game type, so this is the seam a game-specific adapter plugs into rather than
    // something this assembly could implement itself.
    public interface ILegacyImport
    {
        // Whether the legacy data this import knows how to read is still there. Must answer false
        // once Clear() has actually removed it - a stale true here is what would let the import run
        // twice.
        bool IsPresent();

        // Reads the legacy data and reshapes it into a document already at
        // SaveService.CurrentSchemaVersion. Legacy data was never versioned, so there is no old
        // version for the chain to walk forward from - this is a one-time, bespoke conversion, not a
        // migration. Must not touch the legacy data itself; SaveService decides when it is safe to
        // remove it, not this method.
        JObject Import();

        // Removes the legacy data. Only ever called once the document Import() produced has been
        // written and durably persisted under the new save - never on its own, and never before -
        // because the window between those two is where an interruption would otherwise cost a
        // player everything the legacy data held.
        void Clear();
    }
}
