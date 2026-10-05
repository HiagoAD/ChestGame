using System;
using Company.ChestGame.Common;
using Company.ChestGame.Config;
using Company.ChestGame.Config.Internal;
using NUnit.Framework;

namespace Company.ChestGame.Tests.EditMode
{
    /// <summary>
    /// Covers <c>LocalJsonGameConfig</c> parsing and validation. This document carries only what the
    /// whole game shares; the chests values live in ChestsMinigameConfigTests.
    /// </summary>
    /// <remarks>
    /// See docs/architecture.md, "Config pipeline".
    /// </remarks>
    public class LocalJsonGameConfigTests
    {
        [Test]
        public void AValidDocument_PopulatesEveryField()
        {
            LocalJsonGameConfig config = new(@"{
                ""GemsReward"": 3,
                ""CoinsReward"": 120
            }");

            Assert.AreEqual(3, config.GemsReward);
            Assert.AreEqual(120, config.CoinsReward);
        }

        [Test]
        public void AMissingDocument_FailsLoudly()
        {
            GameConfigException error = Assert.Throws<GameConfigException>(() => new LocalJsonGameConfig(null));
            StringAssert.Contains("No game config document", error.Message);
        }

        [Test]
        public void AnEmptyDocument_FailsLoudly()
        {
            Assert.Throws<GameConfigException>(() => new LocalJsonGameConfig(""));
        }

        [Test]
        public void AMalformedDocument_FailsWithATargetedMessage()
        {
            Exception error = Assert.Throws<GameConfigException>(
                () => new LocalJsonGameConfig(@"{ ""GemsReward"": 10, ""CoinsReward"":"));

            StringAssert.Contains("not valid JSON", error.Message);
            Assert.IsNotNull(error.InnerException, "the underlying parse error is kept for diagnostics");
        }

        [Test]
        public void ADocumentThatIsNotAnObject_FailsRatherThanYieldingANullConfig()
        {
            Assert.Throws<GameConfigException>(() => new LocalJsonGameConfig("null"));
        }

        [Test]
        public void UnknownFields_AreIgnoredSoTheConfigCanGrowServerSide()
        {
            LocalJsonGameConfig config = new(@"{
                ""GemsReward"": 10,
                ""CoinsReward"": 50,
                ""ChestCount"": 12,
                ""SomeFieldThisClientHasNeverHeardOf"": true
            }");

            Assert.AreEqual(10, config.GemsReward);
            Assert.AreEqual(50, config.CoinsReward);
        }

        [Test]
        public void ANegativeReward_IsRejected()
        {
            GameConfigException error = Assert.Throws<GameConfigException>(
                () => new LocalJsonGameConfig(DocumentWith(coinsReward: -50)));

            StringAssert.Contains(nameof(GameConfigData.CoinsReward), error.Message);
        }

        [Test]
        public void ANegativeGemsReward_IsRejected()
        {
            GameConfigException error = Assert.Throws<GameConfigException>(
                () => new LocalJsonGameConfig(DocumentWith(gemsReward: -1)));

            StringAssert.Contains(nameof(GameConfigData.GemsReward), error.Message);
        }

        private static string DocumentWith(long gemsReward = 10, long coinsReward = 50) =>
            $@"{{
                ""GemsReward"": {gemsReward},
                ""CoinsReward"": {coinsReward}
            }}";
    }
}
