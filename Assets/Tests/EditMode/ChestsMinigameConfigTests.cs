using System;
using System.Threading;
using Company.ChestGame.Common;
using Company.ChestGame.Minigame.Chests;
using Company.ChestGame.Minigame.Core;
using Company.ChestGame.Tests.Common;
using NUnit.Framework;
using UnityEngine;

namespace Company.ChestGame.Tests.EditMode
{
    // These failures belong to the chests minigame now, not to the game-wide config. The minigame
    // owns its document, so it owns rejecting a document that would describe a round nobody can
    // play: no chests to open, no attempts to open them with, or a negative timer.
    public class ChestsMinigameConfigTests
    {
        [Test]
        public void AValidDocument_PopulatesEveryField()
        {
            ChestsMinigameConfig config = ChestsMinigameConfig.Parse(DocumentWith(
                chestCount: 8, attemptsCount: 5, timeToOpenMilliseconds: 750));

            Assert.AreEqual(8, config.ChestCount);
            Assert.AreEqual(5, config.AttempsCount);
            Assert.AreEqual(750, config.TimeToOpenChestMiliseconds);
        }

        [Test]
        public void AMissingDocument_FailsLoudly()
        {
            GameConfigException error = Assert.Throws<GameConfigException>(() => ChestsMinigameConfig.Parse(null));
            StringAssert.Contains("No chests minigame config document", error.Message);
        }

        [Test]
        public void AnEmptyDocument_FailsLoudly()
        {
            // The message is asserted so this cannot be satisfied by the parsed-to-nothing branch
            // further down, which an empty string would also reach.
            GameConfigException error = Assert.Throws<GameConfigException>(() => ChestsMinigameConfig.Parse(""));
            StringAssert.Contains("No chests minigame config document", error.Message);
        }

        [Test]
        public void AMalformedDocument_FailsWithATargetedMessage()
        {
            // A truncated payload, the shape a half-finished download takes.
            Exception error = Assert.Throws<GameConfigException>(
                () => ChestsMinigameConfig.Parse(@"{ ""ChestCount"": 12, ""AttempsCount"":"));

            StringAssert.Contains("not valid JSON", error.Message);
            Assert.IsNotNull(error.InnerException, "the underlying parse error is kept for diagnostics");
        }

        [Test]
        public void ADocumentThatIsNotAnObject_FailsRatherThanYieldingANullConfig()
        {
            Assert.Throws<GameConfigException>(() => ChestsMinigameConfig.Parse("null"));
        }

        [Test]
        public void UnknownFields_AreIgnoredSoTheConfigCanGrowServerSide()
        {
            ChestsMinigameConfig config = ChestsMinigameConfig.Parse(@"{
                ""ChestCount"": 12,
                ""AttempsCount"": 12,
                ""TimeToOpenChestMiliseconds"": 1000,
                ""SomeFieldThisClientHasNeverHeardOf"": true
            }");

            Assert.AreEqual(12, config.ChestCount);
        }

        [Test]
        public void MissingRequiredFields_AreRejected()
        {
            // Absent fields deserialize to 0, which for AttempsCount means a round that can never
            // end. Rejected at the boundary rather than surfacing as a stuck game later.
            GameConfigException error = Assert.Throws<GameConfigException>(
                () => ChestsMinigameConfig.Parse(@"{ ""ChestCount"": 4 }"));

            StringAssert.Contains(nameof(ChestsMinigameConfig.AttempsCount), error.Message);
        }

        [Test]
        public void AZeroChestCount_IsRejected()
        {
            // A game with no chests can never be played, let alone won.
            GameConfigException error = Assert.Throws<GameConfigException>(
                () => ChestsMinigameConfig.Parse(DocumentWith(chestCount: 0)));

            StringAssert.Contains(nameof(ChestsMinigameConfig.ChestCount), error.Message);
        }

        [Test]
        public void ANegativeChestCount_IsRejected()
        {
            Assert.Throws<GameConfigException>(() => ChestsMinigameConfig.Parse(DocumentWith(chestCount: -3)));
        }

        [Test]
        public void AZeroAttemptsCount_IsRejected()
        {
            // A round with no attempts can never end.
            GameConfigException error = Assert.Throws<GameConfigException>(
                () => ChestsMinigameConfig.Parse(DocumentWith(attemptsCount: 0)));

            StringAssert.Contains(nameof(ChestsMinigameConfig.AttempsCount), error.Message);
        }

        [Test]
        public void ANegativeOpenTime_IsRejected()
        {
            GameConfigException error = Assert.Throws<GameConfigException>(
                () => ChestsMinigameConfig.Parse(DocumentWith(timeToOpenMilliseconds: -1)));

            StringAssert.Contains(nameof(ChestsMinigameConfig.TimeToOpenChestMiliseconds), error.Message);
        }

        [Test]
        public void AnInstantOpenTime_IsAccepted()
        {
            // Zero is a legitimate tuning value, unlike zero chests: chests just open immediately.
            ChestsMinigameConfig config = ChestsMinigameConfig.Parse(DocumentWith(timeToOpenMilliseconds: 0));

            Assert.AreEqual(0, config.TimeToOpenChestMiliseconds);
        }

        [Test]
        public void CreateWithAnOutOfRangeValue_IsRejectedJustLikeTheDocumentRoute()
        {
            // Create is the direct construction path, so it has to be as guarded as Parse.
            // Without it the type would be immutable but still constructible in an invalid state.
            GameConfigException error = Assert.Throws<GameConfigException>(
                () => ChestsMinigameConfig.Create(chestCount: 0, attempsCount: 4, timeToOpenChestMiliseconds: 1000));

            StringAssert.Contains(nameof(ChestsMinigameConfig.ChestCount), error.Message);
        }

        [Test]
        public void ADefinitionWithNoConfigDocument_FailsWithATypedException()
        {
            // An empty inspector slot is the most common authoring mistake there is, and an unwired
            // AssetReference is an empty GUID that would otherwise be reported as a missing asset
            // naming nothing a reader could look up. The failure has to stay inside
            // ChestGameException like every other, or a test asserting it proves nothing.
            //
            // It surfaces from the configure hook rather than from construction now: the document
            // is behind a reference, so nothing about it can be known until the minigame begins.
            ChestsMinigameSO definition = ScriptableObject.CreateInstance<ChestsMinigameSO>();
            try
            {
                MinigameContainer container = definition.GetMinigameContainer();

                GameConfigException error = Assert.Throws<GameConfigException>(() =>
                    SynchronousUniTask.Complete(definition.ConfigureControllerAsync(
                        container.ControllerInstance, new FakeAssetProvider(), CancellationToken.None)));

                StringAssert.Contains("no config document assigned", error.Message);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(definition);
            }
        }

        private static string DocumentWith(int chestCount = 12, int attemptsCount = 12,
            int timeToOpenMilliseconds = 1000) =>
            $@"{{
                ""ChestCount"": {chestCount},
                ""AttempsCount"": {attemptsCount},
                ""TimeToOpenChestMiliseconds"": {timeToOpenMilliseconds}
            }}";
    }
}
