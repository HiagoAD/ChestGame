using System;
using Company.ChestGame.Config;
using Company.ChestGame.Config.Internal;
using Company.ChestGame.Tests.Common;
using NUnit.Framework;

namespace Company.ChestGame.Tests.EditMode
{
    // The failure surface here is the point: a real remote config can hand back nothing, a
    // truncated payload, or a document whose fields have moved. Splitting fetching from parsing is
    // what makes each of those reachable without an actual network or asset.
    public class LocalJsonGameConfigTests
    {
        private FakeGameConfigSource _source;

        [SetUp]
        public void SetUp() => _source = new FakeGameConfigSource();

        [Test]
        public void AValidDocument_PopulatesEveryField()
        {
            _source.Document = @"{
                ""ChestCount"": 8,
                ""AttempsCount"": 5,
                ""TimeToOpenChestMiliseconds"": 750,
                ""GemsReward"": 3,
                ""CoinsReward"": 120
            }";

            LocalJsonGameConfig config = new(_source);

            Assert.AreEqual(8, config.ChestCount);
            Assert.AreEqual(5, config.AttempsCount);
            Assert.AreEqual(750, config.TimeToOpenChestMiliseconds);
            Assert.AreEqual(3, config.GemsReward);
            Assert.AreEqual(120, config.CoinsReward);
            Assert.IsTrue(config.Initialized);
        }

        [Test]
        public void TheDocumentIsReadExactlyOnce()
        {
            _ = new LocalJsonGameConfig(_source);

            Assert.AreEqual(1, _source.ReadCallCount, "config is a singleton; it should not re-fetch per field");
        }

        [Test]
        public void AMissingDocument_FailsLoudly()
        {
            _source.Document = null;

            GameConfigException error = Assert.Throws<GameConfigException>(() => new LocalJsonGameConfig(_source));
            StringAssert.Contains("No game config document", error.Message);
        }

        [Test]
        public void AnEmptyDocument_FailsLoudly()
        {
            _source.Document = "";

            Assert.Throws<GameConfigException>(() => new LocalJsonGameConfig(_source));
        }

        [Test]
        public void AMalformedDocument_FailsWithATargetedMessage()
        {
            // A truncated payload, the shape a half-finished download takes.
            _source.Document = @"{ ""ChestCount"": 12, ""AttempsCount"":";

            Exception error = Assert.Throws<GameConfigException>(() => new LocalJsonGameConfig(_source));
            StringAssert.Contains("not valid JSON", error.Message);
            Assert.IsNotNull(error.InnerException, "the underlying parse error is kept for diagnostics");
        }

        [Test]
        public void ADocumentThatIsNotAnObject_FailsRatherThanYieldingANullConfig()
        {
            _source.Document = "null";

            Assert.Throws<GameConfigException>(() => new LocalJsonGameConfig(_source));
        }

        [Test]
        public void UnknownFields_AreIgnoredSoTheConfigCanGrowServerSide()
        {
            // A server rolling out a new field must not break clients that predate it.
            _source.Document = @"{
                ""ChestCount"": 12,
                ""AttempsCount"": 12,
                ""TimeToOpenChestMiliseconds"": 1000,
                ""GemsReward"": 10,
                ""CoinsReward"": 50,
                ""SomeFieldThisClientHasNeverHeardOf"": true
            }";

            LocalJsonGameConfig config = new(_source);

            Assert.AreEqual(12, config.ChestCount);
            Assert.IsTrue(config.Initialized);
        }

        [Test]
        public void MissingRequiredFields_AreRejected()
        {
            // Absent fields deserialize to 0, which for AttempsCount means a round that can never
            // end. Rejected at the boundary rather than surfacing as a stuck game later.
            _source.Document = @"{ ""ChestCount"": 4 }";

            GameConfigException error = Assert.Throws<GameConfigException>(() => new LocalJsonGameConfig(_source));
            StringAssert.Contains(nameof(GameConfigData.AttempsCount), error.Message);
        }

        [Test]
        public void AZeroChestCount_IsRejected()
        {
            // A game with no chests can never be played, let alone won.
            _source.Document = DocumentWith(chestCount: 0);

            GameConfigException error = Assert.Throws<GameConfigException>(() => new LocalJsonGameConfig(_source));
            StringAssert.Contains(nameof(GameConfigData.ChestCount), error.Message);
        }

        [Test]
        public void ANegativeChestCount_IsRejected()
        {
            _source.Document = DocumentWith(chestCount: -3);

            Assert.Throws<GameConfigException>(() => new LocalJsonGameConfig(_source));
        }

        [Test]
        public void ANegativeReward_IsRejected()
        {
            // A negative reward would be handed to AddCurrency, which rejects and logs an error on
            // every single win.
            _source.Document = DocumentWith(coinsReward: -50);

            GameConfigException error = Assert.Throws<GameConfigException>(() => new LocalJsonGameConfig(_source));
            StringAssert.Contains(nameof(GameConfigData.CoinsReward), error.Message);
        }

        [Test]
        public void AnInstantOpenTime_IsAccepted()
        {
            // Zero is a legitimate tuning value, unlike zero chests: chests just open immediately.
            _source.Document = DocumentWith(timeToOpenMilliseconds: 0);

            LocalJsonGameConfig config = new(_source);

            Assert.AreEqual(0, config.TimeToOpenChestMiliseconds);
        }

        private static string DocumentWith(int chestCount = 12, int attemptsCount = 12,
            int timeToOpenMilliseconds = 1000, long gemsReward = 10, long coinsReward = 50) =>
            $@"{{
                ""ChestCount"": {chestCount},
                ""AttempsCount"": {attemptsCount},
                ""TimeToOpenChestMiliseconds"": {timeToOpenMilliseconds},
                ""GemsReward"": {gemsReward},
                ""CoinsReward"": {coinsReward}
            }}";
    }
}
