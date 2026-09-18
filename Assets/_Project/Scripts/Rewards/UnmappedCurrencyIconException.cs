using Company.ChestGame.Common;
using Company.ChestGame.Currency;

namespace Company.ChestGame.Rewards
{
    /// <summary>
    /// <see cref="RewardReceivedPopup"/> was asked to show a <see cref="CurrencyType"/> it has no
    /// icon sprite mapped for.
    /// </summary>
    /// <remarks>
    /// See docs/architecture.md, "Exception hierarchy".
    /// </remarks>
    public class UnmappedCurrencyIconException : ChestGameException
    {
        public CurrencyType CurrencyType { get; }

        public UnmappedCurrencyIconException(CurrencyType currencyType)
            : base($"No icon sprite is mapped for currency type {currencyType}")
        {
            CurrencyType = currencyType;
        }
    }
}
