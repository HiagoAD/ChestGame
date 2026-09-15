using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Company.ChestGame.Common;
using Company.ChestGame.Minigame.Chests;
using Company.ChestGame.Minigame.Chests.Internal;
using Company.ChestGame.Saving;
using Company.ChestGame.Tests.Common;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Company.ChestGame.Tests.PlayMode
{
    /// <summary>
    /// Covers what only play mode can prove about the chest logic already covered exhaustively in
    /// edit mode against a fake clock: that <see cref="UnityGameClock"/> on the real player loop
    /// drives the same flow.
    /// </summary>
    /// <remarks>
    /// See docs/testing.md, "What lives where".
    /// </remarks>
    public class ChestsMinigameIntegrationTests
    {
        private const int OpenMilliseconds = 200;

        private FakeRewardsManager _rewards;
        private FakeRandomProvider _random;
        private ChestsMinigameController _controller;

        /// <remarks>
        /// See docs/saving.md, "InMemoryStore".
        /// See docs/testing.md, "What lives where".
        /// </remarks>
        [SetUp]
        public void SetUp()
        {
            ChestsMinigameConfig config = ChestsMinigameConfig.Create(
                chestCount: 4, attempsCount: 4, timeToOpenChestMiliseconds: OpenMilliseconds);
            _rewards = new FakeRewardsManager();
            _random = new FakeRandomProvider { NextValue = 1f };
            _controller = new ChestsMinigameController();
            _controller.Configure(config);

            ISaveService saveService = new SaveService(new JsonCodec(), new NoProtection(), new InMemoryStore());
            _controller.Inject(_rewards, _random, new UnityGameClock(), saveService, new SaveFlushRegistry());
        }

        [TearDown]
        public void TearDown() => _controller.Dispose();

        /// <remarks>
        /// See docs/testing.md, "What lives where".
        /// </remarks>
        private static WaitForSeconds SettleTime() => new(OpenMilliseconds / 1000f * 10f);

        [UnityTest]
        public IEnumerator OnTheRealPlayerLoop_AClickedChestOpens()
        {
            _controller.NewGame();

            _controller.OnChestClicked(_controller.Chests[0]);
            yield return SettleTime();

            Assert.AreEqual(ChestsMinigameChestModel.State.Open_Empty, _controller.Chests[0].CurrentState);
            Assert.AreEqual(1, _controller.Attempts);
        }

        [UnityTest]
        public IEnumerator OnTheRealPlayerLoop_SwitchingChestsCancelsTheFirst()
        {
            _controller.NewGame();

            _controller.OnChestClicked(_controller.Chests[0]);
            yield return null;
            _controller.OnChestClicked(_controller.Chests[1]);
            yield return SettleTime();

            Assert.AreEqual(ChestsMinigameChestModel.State.Closed, _controller.Chests[0].CurrentState);
            Assert.AreEqual(ChestsMinigameChestModel.State.Open_Empty, _controller.Chests[1].CurrentState);
            Assert.AreEqual(1, _controller.Attempts);
        }

        /// <remarks>
        /// See docs/minigames.md, "The controller".
        /// See docs/testing.md, "What lives where".
        /// </remarks>
        [UnityTest]
        public IEnumerator OnTheRealPlayerLoop_NoProgressTickLandsAfterAChestOpens()
        {
            _controller.NewGame();

            List<ChestsMinigameChestModel.State> sequence = new();
            _controller.Chests[0].OnStateChanged += sequence.Add;

            _controller.OnChestClicked(_controller.Chests[0]);
            yield return SettleTime();

            CollectionAssert.IsNotEmpty(sequence);

            int openedAt = sequence.FindIndex(state =>
                state is ChestsMinigameChestModel.State.Open_Empty or ChestsMinigameChestModel.State.Open_Prize);

            Assert.GreaterOrEqual(openedAt, 0, "the chest should have opened within the settle window");
            CollectionAssert.DoesNotContain(sequence.GetRange(openedAt, sequence.Count - openedAt),
                ChestsMinigameChestModel.State.Opening,
                "an opened chest must never report Opening again");
        }

        [UnityTest]
        public IEnumerator OnTheRealPlayerLoop_AFullRoundEndsInAWin()
        {
            _controller.NewGame();

            bool? outcome = null;
            _controller.OnGameFinished += won => outcome = won;

            for (int i = 0; i < _controller.Chests.Count && outcome == null; i++)
            {
                _controller.OnChestClicked(_controller.Chests[i]);
                yield return SettleTime();
            }

            Assert.AreEqual(true, outcome);
            CollectionAssert.AreEqual(new[] { "ChestsMinigame" }, _rewards.GiveRewardCalls);
            Assert.AreEqual(1, _controller.Chests.Count(c => c.CurrentState == ChestsMinigameChestModel.State.Open_Prize));
        }
    }
}
