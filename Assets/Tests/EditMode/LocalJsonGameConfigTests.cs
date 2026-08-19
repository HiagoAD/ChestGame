using System;
using Company.ChestGame.Common;
using Company.ChestGame.Config;
using Company.ChestGame.Config.Internal;
using Company.ChestGame.Tests.Common;
using NUnit.Framework;

namespace Company.ChestGame.Tests.EditMode
{
    // The failure surface here is the point: a real remote config can hand back nothing, a
    // truncated payload, or a document whose fields have moved. Splitting fetching from parsing is
    // what makes each of those reachable without an actual network or asset.
    //
    // What this document carries is now only what the whole game shares. The chests minigame's own
    // values, and their range rules, live in ChestsMinigameConfigTests.
    public class LocalJsonGameConfigTests
    {
        private FakeGameConfigSource _source;

        [SetUp]
        public void SetUp() => _source = new FakeGameConfigSource();

        [Test]
        public void AValidDocument_PopulatesEveryField()
        {
            _source.Document = @"{
                ""GemsReward"": 3,
                ""CoinsReward"": 120
            }";

            LocalJsonGameConfig config = new(_source);

            Assert.AreEqual(3, config.GemsReward);
            Assert.AreEqual(120, config.CoinsReward);
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
            _source.Document = @"{ ""GemsReward"": 10, ""CoinsReward"":";

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
            // A server rolling out a new field must not break clients that predate it. The chests
            // minigame's own fields are one such case now: this document no longer knows them.
            _source.Document = @"{
                ""GemsReward"": 10,
                ""CoinsReward"": 50,
                ""ChestCount"": 12,
                ""SomeFieldThisClientHasNeverHeardOf"": true
            }";

            LocalJsonGameConfig config = new(_source);

            Assert.AreEqual(10, config.GemsReward);
            Assert.AreEqual(50, config.CoinsReward);
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
        public void ANegativeGemsReward_IsRejected()
        {
            _source.Document = DocumentWith(gemsReward: -1);

            GameConfigException error = Assert.Throws<GameConfigException>(() => new LocalJsonGameConfig(_source));
            StringAssert.Contains(nameof(GameConfigData.GemsReward), error.Message);
        }

        [Test]
        public void AZeroReward_IsAccepted()
        {
            // Zero is a legitimate tuning value: a currency the game currently gives none of.
            _source.Document = DocumentWith(gemsReward: 0, coinsReward: 0);

            LocalJsonGameConfig config = new(_source);

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
