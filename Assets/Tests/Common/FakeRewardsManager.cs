using System;
using System.Collections.Generic;
using Company.ChestGame.Currency;
using Company.ChestGame.Rewards;

namespace Company.ChestGame.Tests.Common
{
    /// <summary>
    /// Records reward requests instead of granting anything. <see cref="RewardToGive"/> controls what
    /// the <see cref="OnCurrencyRewardGiven"/> event reports, for tests that observe downstream
    /// listeners.
    /// </summary>
    public class FakeRewardsManager : IRewardsManager
    {
        public event Action<CurrencyType, long, string> OnCurrencyRewardGiven;

        public readonly List<string> GiveRewardCalls = new();

        public CurrencyType RewardToGive { get; set; } = CurrencyType.Coins;
        public long AmountToGive { get; set; } = 50;

        public void GiveRandomCurrencyReward(string source)
        {
            GiveRewardCalls.Add(source);
            OnCurrencyRewardGiven?.Invoke(RewardToGive, AmountToGive, source);
        }
    }
}
