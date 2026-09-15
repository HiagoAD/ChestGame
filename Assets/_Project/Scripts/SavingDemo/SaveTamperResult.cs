namespace Company.ChestGame.Saving.Demo
{
    /// <summary>
    /// What a reload did after <see cref="SaveTamper"/> edited a stored save in place.
    /// </summary>
    /// <remarks>
    /// See docs/saving.md, "Tamper detection is a different failure from a corrupt payload".
    /// </remarks>
    public enum SaveTamperOutcome
    {
        Loaded,
        RejectedAsTampered,
        RejectedAsUnreadable
    }

    public readonly struct SaveTamperResult
    {
        public SaveTamperOutcome Outcome { get; }
        public long? LoadedBalance { get; }
        public SaveException Error { get; }

        public SaveTamperResult(SaveTamperOutcome outcome, long? loadedBalance, SaveException error)
        {
            Outcome = outcome;
            LoadedBalance = loadedBalance;
            Error = error;
        }
    }
}
