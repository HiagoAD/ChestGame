using System;
using Company.ChestGame.Config;
using Company.ChestGame.Currency;
using Company.ChestGame.Popups;
using VContainer;

using Random = UnityEngine.Random;

namespace Company.ChestGame.Rewards
{
    public class RewardsManager : IRewardsManager
    {
        public event Action<CurrencyType, long, string> OnCurrencyRewardGiven;

        readonly private ICurrencyManager _currencyManager;
        readonly private IGameConfig _gameConfig;
        readonly private IPopupManager _popupManager;

        public RewardsManager(ICurrencyManager currencyManager, IGameConfig gameConfig, IPopupManager popupManager)
        {
            _currencyManager = currencyManager;
            _gameConfig = gameConfig;
            _popupManager = popupManager;
        }


        public void GiveRandomCurrencyReward(string source)
        {
            CurrencyType currencyType = (CurrencyType)Random.Range(0, Enum.GetValues(typeof(CurrencyType)).Length);

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