namespace Company.ChestGame.Saving
{
    // Which IPayloadProtector a profile wants, in a form an inspector can serialize.
    //
    // Append only, for the same reason SaveStorage is: a SaveProfileSO stores this by index, so
    // inserting a member in the middle silently repoints every authored profile at a different
    // protector. Phase 3 appends here; None keeps index 0. See docs/saving.md.
    public enum SaveProtection
    {
        None
    }
}
