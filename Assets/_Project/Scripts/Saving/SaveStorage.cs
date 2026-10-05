namespace Company.ChestGame.Saving
{
    /// <summary>
    /// Which <see cref="ISaveStore"/> a profile wants, in a form an inspector can serialize.
    /// </summary>
    /// <remarks>
    /// Append only. A <see cref="SaveProfileSO"/> stores this by index, so inserting a member in
    /// the middle silently repoints every authored profile at a different backend. A new backend
    /// goes after <see cref="InMemory"/>, not before it.
    /// See docs/saving.md, "The three selection enums are append-only".
    /// </remarks>
    public enum SaveStorage
    {
        File,
        AtomicFile,
        PlayerPrefs,
        InMemory
    }
}
