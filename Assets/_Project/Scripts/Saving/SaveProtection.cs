namespace Company.ChestGame.Saving
{
    // Which IPayloadProtector a profile wants, in a form an inspector can serialize.
    //
    // Append only: a SaveProfileSO stores this by index, so inserting a member in the middle
    // silently repoints every authored profile at a different protector. None keeps index 0; add a
    // new protector after the last existing member, never between two existing ones.
    public enum SaveProtection
    {
        None,
        Base64,
        Xor,
        Hmac,
        Aes
    }
}
