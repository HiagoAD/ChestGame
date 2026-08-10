using System;
using System.Collections.Generic;
using Company.ChestGame.Currency;
using Company.ChestGame.Rewards;

namespace Company.ChestGame.Tests.Common
{
    // Records reward requests instead of granting anything. RewardToGive controls what the
    // OnCurrencyRewardGiven event reports, for tests that observe downstream listeners.
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
