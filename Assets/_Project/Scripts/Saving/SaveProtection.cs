namespace Company.ChestGame.Saving
{
    /// <summary>
    /// Which <see cref="IPayloadProtector"/> a profile wants, in a form an inspector can serialize.
    /// </summary>
    /// <remarks>
    /// Append only: a <see cref="SaveProfileSO"/> stores this by index, so inserting a member in
    /// the middle silently repoints every authored profile at a different protector. None keeps
    /// index 0; add a new protector after the last existing member, never between two existing
    /// ones.
    /// See docs/saving.md, "The three selection enums are append-only".
    /// </remarks>
    public enum SaveProtection
    {
        None,
        Base64,
        Xor,
        Hmac,
        Aes
    }
}
