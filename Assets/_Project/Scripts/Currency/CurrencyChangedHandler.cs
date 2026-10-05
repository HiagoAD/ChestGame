namespace Company.ChestGame.Currency
{
    /// <summary>
    /// Listener for a currency event.
    /// </summary>
    /// <param name="currency">The currency that changed.</param>
    /// <param name="amount">
    /// Positive on Collected and Spent, and signed on Changed (negative for a spend).
    /// </param>
    /// <param name="balance">The balance after the change.</param>
    /// <param name="source">What caused the change.</param>
    public delegate void CurrencyChangedHandler(CurrencyType currency, long amount, long balance, string source);
}
