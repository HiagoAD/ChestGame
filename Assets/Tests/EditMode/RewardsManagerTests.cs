using System;
using System.Collections.Generic;
using Company.ChestGame.Common;
using Company.ChestGame.Config;
using System.Text.RegularExpressions;
using Company.ChestGame.Currency;
using Company.ChestGame.Rewards;
using Company.ChestGame.Tests.Common;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Company.ChestGame.Tests.EditMode
{
    /// <summary>
    /// Covers <see cref="RewardsManager"/> against fakes for currency, config, popups and the
    /// random draw.
    /// </summary>
    /// <remarks>
    /// See docs/testing.md, "RewardsManagerTests, and pinning the random draw".
    /// </remarks>
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

        [Test]
        public void GiveRandomCurrencyReward_WhenTheDrawnCurrencyHasNoMapping_ThrowsNamingItAndDoesNothingElse()
        {
            int unmapped = Enum.GetValues(typeof(CurrencyType)).Length;
            _random.NextRangeResult = unmapped;
            List<CurrencyType> announced = new();
            _rewards.OnCurrencyRewardGiven += (c, a, s) => announced.Add(c);

            UnmappedCurrencyRewardException exception = Assert.Throws<UnmappedCurrencyRewardException>(
                () => _rewards.GiveRandomCurrencyReward("ChestsMinigame"));

            Assert.AreEqual((CurrencyType)unmapped, exception.CurrencyType);
            StringAssert.Contains(((CurrencyType)unmapped).ToString(), exception.Message);
            CollectionAssert.IsEmpty(_currency.AddCalls);
            CollectionAssert.IsEmpty(_popups.SpawnCalls);
            CollectionAssert.IsEmpty(announced);
        }

        /// <summary>
        /// Runs a document through the real <c>LocalJsonGameConfig</c>, <c>RewardsManager</c> and
        /// <c>CurrencyManager</c>: the config either refuses the document, or the reward it describes
        /// is one the bank takes.
        /// </summary>
        /// <remarks>
        /// See docs/architecture.md, "Config pipeline".
        /// </remarks>
        [TestCase(CurrencyType.Coins, @"{ ""GemsReward"": 10, ""CoinsReward"": 0 }")]
        [TestCase(CurrencyType.Gems, @"{ ""GemsReward"": 0, ""CoinsReward"": 50 }")]
        public void AConfigLocalJsonGameConfigAccepts_NeverYieldsARewardTheRealCurrencyManagerRejects(CurrencyType drawn, string document)
        {
            LocalJsonGameConfig config;
            try
            {
                config = new LocalJsonGameConfig(document);
            }
            catch (GameConfigException)
            {
                return;
            }

            CurrencyManager currency = new(new InMemoryCurrencySaveHandler());
            FakePopupManager popups = new();
            FakeRandomProvider random = new() { NextRangeResult = (int)drawn };
            RewardsManager rewards = new(currency, config, popups, random);

            List<long> announced = new();
            rewards.OnCurrencyRewardGiven += (c, a, s) => announced.Add(a);

            rewards.GiveRandomCurrencyReward("ChestsMinigame");

            for (int i = 0; i < popups.SpawnCalls.Count; i++)
            {
                if (popups.SpawnCalls[i].data is RewardReceivedPopupData reward)
                {
                    Assert.Greater(reward.Amount, 0,
                        $"the config accepted a {drawn} reward of {reward.Amount}, and the player was shown it as a reward");
                    Assert.AreEqual(reward.Amount, currency.GetCurrencyAmount(drawn),
                        "whatever the popup announces has to be what actually reached the bank");
                }
            }

            foreach (long amount in announced)
            {
                Assert.Greater(amount, 0, $"the config accepted a {drawn} reward of {amount}, and it was announced as given");
            }
        }

        /// <remarks>
        /// See docs/saving.md, "Save, then notify, for both operations".
        /// </remarks>
        [Test]
        public void GiveRandomCurrencyReward_WhenACurrencyListenerThrows_StillCreditsShowsThePopupAndAnnounces()
        {
            InMemoryCurrencySaveHandler saveHandler = new();
            CurrencyManager realCurrency = new(saveHandler);
            RewardsManager rewards = new(realCurrency, _config, _popups, _random);
            _random.NextRangeResult = (int)CurrencyType.Coins;
            realCurrency.OnCurrencyChanged += (c, a, b, s) => throw new InvalidOperationException("label broken");
            List<(CurrencyType currency, long amount, string source)> announced = new();
            rewards.OnCurrencyRewardGiven += (c, a, s) => announced.Add((c, a, s));
            LogAssert.Expect(LogType.Exception, new Regex("label broken"));

            Assert.DoesNotThrow(() => rewards.GiveRandomCurrencyReward("ChestsMinigame"));

            Assert.AreEqual(1, _popups.SpawnCalls.Count);
            CollectionAssert.AreEqual(new[] { (CurrencyType.Coins, 50L, "ChestsMinigame") }, announced);
            Assert.AreEqual(50, realCurrency.GetCurrencyAmount(CurrencyType.Coins));
            Assert.AreEqual(50, saveHandler.Stored.ResourceAmount[CurrencyType.Coins]);
        }
    }
}
