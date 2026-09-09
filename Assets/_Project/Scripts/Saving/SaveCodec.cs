namespace Company.ChestGame.Saving
{
    // Which ISaveCodec a profile wants, in a form an inspector can serialize.
    //
    // Append only: a SaveProfileSO stores this by index, so inserting a member in the middle
    // silently repoints every authored profile at a different codec. Json keeps index 0; add a new
    // codec after the last existing member, never between two existing ones.
    public enum SaveCodec
    {
        Json,
        JsonPretty,
        JsonGzip
    }
}
