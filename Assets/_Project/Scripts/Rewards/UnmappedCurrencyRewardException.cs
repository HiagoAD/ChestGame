using System;
using Company.ChestGame.Currency;

namespace Company.ChestGame.Rewards
{
    /// <summary>
    /// <see cref="RewardsManager"/> was asked to reward a <see cref="CurrencyType"/> it has no
    /// reward amount mapped for.
    /// </summary>
    /// <remarks>
    /// See docs/architecture.md, "Exception hierarchy".
    /// </remarks>
    public class UnmappedCurrencyRewardException : InvalidOperationException
    {
        public CurrencyType CurrencyType { get; }

        public UnmappedCurrencyRewardException(CurrencyType currencyType)
            : base($"No reward is mapped for currency type {currencyType}")
        {
            CurrencyType = currencyType;
        }
    }
}
