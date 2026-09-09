namespace Company.ChestGame.Saving
{
    // Bytes provably changed after a protector signed or encrypted them, rather than bytes this
    // build merely cannot parse. A subclass rather than a flag, so every existing
    // catch (SaveException) keeps catching it unchanged. See docs/saving.md, "Tamper detection is a
    // different failure from a corrupt payload".
    public sealed class SaveTamperedException : SaveException
    {
        public SaveTamperedException(string message) : base(message) { }
    }
}
