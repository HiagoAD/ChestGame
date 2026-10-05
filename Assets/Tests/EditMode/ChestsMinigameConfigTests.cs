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

        /// <remarks>
        /// See docs/minigames.md, "Its own config document".
        /// </remarks>
        [Test]
        public void AnEmptyDocument_FailsLoudly()
        {
            GameConfigException error = Assert.Throws<GameConfigException>(() => ChestsMinigameConfig.Parse(""));
            StringAssert.Contains("No chests minigame config document", error.Message);
        }

        /// <remarks>
        /// See docs/minigames.md, "Its own config document".
        /// </remarks>
        [Test]
        public void AMalformedDocument_FailsWithATargetedMessage()
        {
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

        /// <remarks>
        /// See docs/minigames.md, "Its own config document".
        /// </remarks>
        [Test]
        public void MissingRequiredFields_AreRejected()
        {
            GameConfigException error = Assert.Throws<GameConfigException>(
                () => ChestsMinigameConfig.Parse(@"{ ""ChestCount"": 4 }"));

            StringAssert.Contains(nameof(ChestsMinigameConfig.AttempsCount), error.Message);
        }

        /// <remarks>
        /// See docs/minigames.md, "Its own config document".
        /// </remarks>
        [Test]
        public void AZeroChestCount_IsRejected()
        {
            GameConfigException error = Assert.Throws<GameConfigException>(
                () => ChestsMinigameConfig.Parse(DocumentWith(chestCount: 0)));

            StringAssert.Contains(nameof(ChestsMinigameConfig.ChestCount), error.Message);
        }

        [Test]
        public void ANegativeChestCount_IsRejected()
        {
            Assert.Throws<GameConfigException>(() => ChestsMinigameConfig.Parse(DocumentWith(chestCount: -3)));
        }

        /// <remarks>
        /// See docs/minigames.md, "Its own config document".
        /// </remarks>
        [Test]
        public void AZeroAttemptsCount_IsRejected()
        {
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

        /// <remarks>
        /// See docs/minigames.md, "Its own config document".
        /// </remarks>
        [Test]
        public void AnInstantOpenTime_IsAccepted()
        {
            ChestsMinigameConfig config = ChestsMinigameConfig.Parse(DocumentWith(timeToOpenMilliseconds: 0));

            Assert.AreEqual(0, config.TimeToOpenChestMiliseconds);
        }

        /// <remarks>
        /// See docs/minigames.md, "Its own config document".
        /// </remarks>
        [Test]
        public void CreateWithAnOutOfRangeValue_IsRejectedJustLikeTheDocumentRoute()
        {
            GameConfigException error = Assert.Throws<GameConfigException>(
                () => ChestsMinigameConfig.Create(chestCount: 0, attempsCount: 4, timeToOpenChestMiliseconds: 1000));

            StringAssert.Contains(nameof(ChestsMinigameConfig.ChestCount), error.Message);
        }

        /// <remarks>
        /// See docs/minigames.md, "Its own config document".
        /// </remarks>
        [Test]
        public void ADefinitionWithNoConfigDocument_FailsWithATypedException()
        {
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
