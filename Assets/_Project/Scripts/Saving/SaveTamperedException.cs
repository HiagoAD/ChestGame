namespace Company.ChestGame.Saving
{
    /// <summary>
    /// Bytes provably changed after a protector signed or encrypted them, rather than bytes this
    /// build merely cannot parse.
    /// </summary>
    /// <remarks>
    /// See docs/saving.md, "Tamper detection is a different failure from a corrupt payload".
    /// </remarks>
    public sealed class SaveTamperedException : SaveException
    {
        public SaveTamperedException(string message) : base(message) { }
    }
}
