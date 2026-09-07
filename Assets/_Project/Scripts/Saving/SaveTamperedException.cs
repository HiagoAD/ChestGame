namespace Company.ChestGame.Saving
{
    // The typed half of a distinction SaveException's PayloadTampered factory already claimed in
    // prose: bytes provably changed after a protector signed or encrypted them, rather than bytes
    // this build merely cannot parse. A subclass rather than a flag, so every existing
    // catch (SaveException) keeps catching it unchanged. See docs/saving.md, "Tamper detection is a
    // different failure from a corrupt payload".
    public sealed class SaveTamperedException : SaveException
    {
        public SaveTamperedException(string message) : base(message) { }
    }
}
