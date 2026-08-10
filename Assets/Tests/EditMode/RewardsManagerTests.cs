using System;
using System.Collections.Generic;
using Company.ChestGame.Currency;
using Company.ChestGame.Rewards;
using Company.ChestGame.Tests.Common;
using NUnit.Framework;

namespace Company.ChestGame.Tests.EditMode
{
    // Reward selection is a random draw over the CurrencyType enum; FakeRandomProvider pins the
    // draw so each branch can be asserted exactly rather than statistically.
    public class RewardsManagerTests
    {
        private FakeCurrencyManager _currency;
        private FakeGameConfig _config;
        private FakePopupManager _popups;
        private FakeRandomProvider _random;
        private RewardsManager _rewards;

        [SetUp]
        public void SetUp()
        {
            _currency = new FakeCurrencyManager();
            _config = new FakeGameConfig { CoinsReward = 50, GemsReward = 10 };
            _popups = new FakePopupManager();
            _random = new FakeRandomProvider();
            _rewards = new RewardsManager(_currency, _config, _popups, _random);
        }

        [Test]
        public void GiveRandomCurrencyReward_DrawsAcrossTheWholeCurrencyEnum()
        {
            _rewards.GiveRandomCurrencyReward("ChestsMinigame");

            int currencyCount = Enum.GetValues(typeof(CurrencyType)).Length;
            CollectionAssert.AreEqual(new[] { (0, currencyCount) }, _random.RangeCalls);
        }

        [Test]
        public void GiveRandomCurrencyReward_WhenCoinsAreDrawn_GrantsTheConfiguredCoinAmount()
        {
            _random.NextRangeResult = (int)CurrencyType.Coins;

            _rewards.GiveRandomCurrencyReward("ChestsMinigame");

            CollectionAssert.AreEqual(new[] { (CurrencyType.Coins, 50L, "ChestsMinigame") }, _currency.AddCalls);
            Assert.AreEqual(50, _currency.GetCurrencyAmount(CurrencyType.Coins));
        }

        [Test]
        public void GiveRandomCurrencyReward_WhenGemsAreDrawn_GrantsTheConfiguredGemAmount()
        {
            _random.NextRangeResult = (int)CurrencyType.Gems;

            _rewards.GiveRandomCurrencyReward("ChestsMinigame");

            CollectionAssert.AreEqual(new[] { (CurrencyType.Gems, 10L, "ChestsMinigame") }, _currency.AddCalls);
            Assert.AreEqual(10, _currency.GetCurrencyAmount(CurrencyType.Gems));
        }

        [Test]
        public void GiveRandomCurrencyReward_ShowsAPopupDescribingTheSameReward()
        {
            _random.NextRangeResult = (int)CurrencyType.Gems;

            _rewards.GiveRandomCurrencyReward("ChestsMinigame");

            Assert.AreEqual(1, _popups.SpawnCalls.Count);
            Assert.AreEqual(typeof(RewardReceivedPopup), _popups.SpawnCalls[0].popupType);

            RewardReceivedPopupData data = (RewardReceivedPopupData)_popups.SpawnCalls[0].data;
            Assert.AreEqual(CurrencyType.Gems, data.CurrencyType);
            Assert.AreEqual(10, data.Amount);
        }

        [Test]
        public void GiveRandomCurrencyReward_AnnouncesTheRewardWithItsSource()
        {
            _random.NextRangeResult = (int)CurrencyType.Coins;
            List<(CurrencyType currency, long amount, string source)> announced = new();
            _rewards.OnCurrencyRewardGiven += (c, a, s) => announced.Add((c, a, s));

            _rewards.GiveRandomCurrencyReward("DailyBonus");

            CollectionAssert.AreEqual(new[] { (CurrencyType.Coins, 50L, "DailyBonus") }, announced);
        }

        [Test]
        public void GiveRandomCurrencyReward_GrantsPopupsAndEventThatAllAgree()
        {
            _random.RangeSequence.Enqueue((int)CurrencyType.Gems);
            _random.RangeSequence.Enqueue((int)CurrencyType.Coins);
            List<(CurrencyType currency, long amount, string source)> announced = new();
            _rewards.OnCurrencyRewardGiven += (c, a, s) => announced.Add((c, a, s));

            _rewards.GiveRandomCurrencyReward("ChestsMinigame");
            _rewards.GiveRandomCurrencyReward("ChestsMinigame");

            for (int i = 0; i < 2; i++)
            {
                RewardReceivedPopupData popupData = (RewardReceivedPopupData)_popups.SpawnCalls[i].data;

                Assert.AreEqual(_currency.AddCalls[i].currency, popupData.CurrencyType);
                Assert.AreEqual(_currency.AddCalls[i].amount, popupData.Amount);
                Assert.AreEqual(_currency.AddCalls[i].currency, announced[i].currency);
                Assert.AreEqual(_currency.AddCalls[i].amount, announced[i].amount);
            }
        }
    }
}
