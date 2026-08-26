using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Company.ChestGame.Common;
using Company.ChestGame.Minigame.Chests;
using Company.ChestGame.Minigame.Chests.Internal;
using Company.ChestGame.Tests.Common;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Company.ChestGame.Tests.PlayMode
{
    // The chest logic is covered exhaustively in edit mode against a fake clock. What only play
    // mode can prove is that UnityGameClock on the player loop drives the same flow. Settled states
    // rather than mid-flight ones, so a slow frame cannot turn into a spurious failure.
    public class ChestsMinigameIntegrationTests
    {
        private const int OpenMilliseconds = 200;

        private FakeRewardsManager _rewards;
        private FakeRandomProvider _random;
        private ChestsMinigameController _controller;

        [SetUp]
        public void SetUp()
        {
            ChestsMinigameConfig config = ChestsMinigameConfig.Create(
                chestCount: 4, attempsCount: 4, timeToOpenChestMiliseconds: OpenMilliseconds);
            _rewards = new FakeRewardsManager();
            _random = new FakeRandomProvider { NextValue = 1f };
            _controller = new ChestsMinigameController();
            _controller.Configure(config);
            _controller.Inject(_rewards, _random, new UnityGameClock());
        }

        [TearDown]
        public void TearDown() => _controller.Dispose();

        // Ten times the open duration: enough slack for a domain reload or a cold CI runner, still
        // bounded.
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

        [UnityTest]
        public IEnumerator OnTheRealPlayerLoop_NoProgressTickLandsAfterAChestOpens()
        {
            // The progress loop and the open timer resume in the same frame and nothing promises
            // which goes first, so a late progress tick would flip an opened chest back to Opening.
            // The edit-mode suite cannot catch that, because its clock chooses the ordering.
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
