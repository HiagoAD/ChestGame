namespace Company.ChestGame.Saving
{
    // Which ISaveCodec a profile wants, in a form an inspector can serialize.
    //
    // Append only, for the same reason SaveStorage is: a SaveProfileSO stores this by index, so
    // inserting a member in the middle silently repoints every authored profile at a different
    // codec. Json keeps index 0 for that reason - phase 3 appended JsonPretty and JsonGzip after
    // it rather than before. See docs/saving.md.
    public enum SaveCodec
    {
        Json,
        JsonPretty,
        JsonGzip
    }
}
