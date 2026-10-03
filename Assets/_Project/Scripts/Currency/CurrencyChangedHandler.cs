namespace Company.ChestGame.Currency
{
    // amount is positive on Collected and Spent, and signed on Changed (negative for a spend);
    // balance is the balance after the change.
    public delegate void CurrencyChangedHandler(CurrencyType currency, long amount, long balance, string source);
}
