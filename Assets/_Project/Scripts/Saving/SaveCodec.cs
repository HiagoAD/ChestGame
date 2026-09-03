namespace Company.ChestGame.Saving
{
    // Which ISaveCodec a profile wants, in a form an inspector can serialize.
    //
    // Append only, for the same reason SaveStorage is: a SaveProfileSO stores this by index, so
    // inserting a member in the middle silently repoints every authored profile at a different
    // codec. Phase 3 appends here; Json keeps index 0. See docs/saving.md.
    public enum SaveCodec
    {
        Json
    }
}
