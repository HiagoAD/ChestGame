namespace Company.ChestGame.Saving
{
    // Which ISaveStore a profile wants, in a form an inspector can serialize.
    //
    // Append only. A SaveProfileSO stores this by index, so inserting a member in the middle
    // silently repoints every authored profile at a different backend. File sits first because
    // first place is what a newly serialized field lands on; a fifth backend goes after InMemory,
    // not before it. See docs/saving.md.
    public enum SaveStorage
    {
        File,
        AtomicFile,
        PlayerPrefs,
        InMemory
    }
}
