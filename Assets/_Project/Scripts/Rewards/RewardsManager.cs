using System;
using Company.ChestGame.Common;
using Company.ChestGame.Config;
using Company.ChestGame.Currency;
using Company.ChestGame.Popups;
using VContainer;

namespace Company.ChestGame.Rewards
{
    public class RewardsManager : IRewardsManager
    {
        public event Action<CurrencyType, long, string> OnCurrencyRewardGiven;

        readonly private ICurrencyManager _currencyManager;
        readonly private IGameConfig _gameConfig;
        readonly private IPopupManager _popupManager;
        readonly private IRandomProvider _random;

        public RewardsManager(ICurrencyManager currencyManager, IGameConfig gameConfig, IPopupManager popupManager, IRandomProvider random)
        {
            _currencyManager = currencyManager;
            _gameConfig = gameConfig;
            _popupManager = popupManager;
            _random = random;
        }


        public void GiveRandomCurrencyReward(string source)
        {
            CurrencyType currencyType = (CurrencyType)_random.Range(0, Enum.GetValues(typeof(CurrencyType)).Length);

            long amount = currencyType switch
            {
                CurrencyType.Coins => _gameConfig.CoinsReward,
                CurrencyType.Gems => _gameConfig.GemsReward,
                _ => throw new NotImplementedException()
            };

            _currencyManager.AddCurrency(currencyType, amount, source);

            _popupManager.Spawn<RewardReceivedPopup, RewardReceivedPopupData>(new RewardReceivedPopupData(currencyType, amount));


            OnCurrencyRewardGiven?.Invoke(currencyType, amount, source);
        }
    }
}