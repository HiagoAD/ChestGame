using System;
using Company.ChestGame.Currency;

namespace Company.ChestGame.Rewards
{
    public interface IRewardsManager
    {
        public event Action<CurrencyType, long, string> OnCurrencyRewardGiven;
        public void GiveRandomCurrencyReward(string source);
    }
}