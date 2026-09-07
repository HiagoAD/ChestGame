namespace Company.ChestGame.Saving.Demo
{
    // What a reload did after SaveTamper edited a stored save in place. RejectedAsTampered and
    // RejectedAsUnreadable are kept apart because SaveException reports them as different findings -
    // see docs/saving.md, "Tamper detection is a different failure from a corrupt payload".
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
