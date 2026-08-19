using System;
using Company.ChestGame.Common;
using Company.ChestGame.Config;
using Company.ChestGame.Config.Internal;
using NUnit.Framework;

namespace Company.ChestGame.Tests.EditMode
{
    // The failure surface here is the point: a real remote config can hand back nothing, a
    // truncated payload, or a document whose fields have moved. Splitting fetching from parsing is
    // what makes each of those reachable without an actual network or asset.
    //
    // The config takes the document rather than a source, so parsing stays a synchronous
    // constructor and none of this needs a task. That the document is fetched exactly once, from
    // every source, is GameContentLoaderTests' business now.
    //
    // What this document carries is now only what the whole game shares. The chests minigame's own
    // values, and their range rules, live in ChestsMinigameConfigTests.
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
            // A truncated payload, the shape a half-finished download takes.
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
            // A server rolling out a new field must not break clients that predate it. The chests
            // minigame's own fields are one such case now: this document no longer knows them.
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
            // A negative reward would be handed to AddCurrency, which rejects and logs an error on
            // every single win.
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

        [Test]
        public void AZeroReward_IsAccepted()
        {
            // Zero is a legitimate tuning value: a currency the game currently gives none of.
            LocalJsonGameConfig config = new(DocumentWith(gemsReward: 0, coinsReward: 0));

            Assert.AreEqual(0, config.GemsReward);
            Assert.AreEqual(0, config.CoinsReward);
        }

        private static string DocumentWith(long gemsReward = 10, long coinsReward = 50) =>
            $@"{{
                ""GemsReward"": {gemsReward},
                ""CoinsReward"": {coinsReward}
            }}";
    }
}
