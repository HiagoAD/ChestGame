using System.Collections.Generic;
using System.Linq;
using Company.ChestGame.Minigame.Chests.Internal;
using Company.ChestGame.Tests.Common;
using NUnit.Framework;

namespace Company.ChestGame.Tests.EditMode
{
    // Everything here goes through the real entry point, OnChestClicked, including the two
    // concurrent UniTasks it spawns. FakeGameClock is what makes that possible in edit mode: the
    // opening flow parks on the clock, and the test decides when time moves.
    //
    // The timings are chosen so a chest takes exactly two frames to open: a 100ms open at 50ms per
    // frame. Frame 1 is mid-flight, frame 2 completes it.
    public class ChestsMinigameControllerTests
    {
        private const int OpenMilliseconds = 100;
        private const int FramesToOpen = 2;

        private FakeGameConfig _config;
        private FakeRewardsManager _rewards;
        private FakeRandomProvider _random;
        private FakeGameClock _clock;
        private ChestsMinigameController _controller;

        [SetUp]
        public void SetUp()
        {
            _config = new FakeGameConfig
            {
                ChestCount = 4,
                AttempsCount = 4,
                TimeToOpenChestMiliseconds = OpenMilliseconds
            };
            _rewards = new FakeRewardsManager();
            _random = new FakeRandomProvider();
            _clock = new FakeGameClock { DeltaTime = 0.05f };
            _controller = new ChestsMinigameController();
        }

        [TearDown]
        public void TearDown() => _controller.Dispose();

        private void Inject() => _controller.Inject(_config, _rewards, _random, _clock);

        // Clicks a chest and lets it run all the way to open.
        private void OpenChest(int index)
        {
            _controller.OnChestClicked(_controller.Chests[index]);
            _clock.AdvanceFrames(FramesToOpen);
        }

        private ChestsMinigameChestModel.State StateOf(int index) => _controller.Chests[index].CurrentState;

        // --- Injection ---------------------------------------------------------------------

        [Test]
        public void Inject_BuildsOneChestPerConfiguredChestCount()
        {
            _config.ChestCount = 7;

            Inject();

            Assert.AreEqual(7, _controller.Chests.Count);
            Assert.IsTrue(_controller.Chests.All(c => c.CurrentState == ChestsMinigameChestModel.State.Closed));
        }

        [Test]
        public void Inject_TakesTotalAttemptsFromConfig_AndStartsIdle()
        {
            _config.AttempsCount = 5;

            Inject();

            Assert.AreEqual(5, _controller.TotalAttempts);
            Assert.AreEqual(0, _controller.Attempts);
            Assert.AreEqual(ChestsMinigameController.State.NotStarted, _controller.CurrentState);
        }

        // --- New game ----------------------------------------------------------------------

        [Test]
        public void NewGame_EntersPlayingAndAnnouncesIt()
        {
            Inject();
            List<ChestsMinigameController.State> states = new();
            _controller.OnStateChange += states.Add;

            _controller.NewGame();

            Assert.AreEqual(ChestsMinigameController.State.Playing, _controller.CurrentState);
            CollectionAssert.AreEqual(new[] { ChestsMinigameController.State.Playing }, states);
        }

        [Test]
        public void NewGame_ResetsAttemptsAndAnnouncesTheNewCount()
        {
            Inject();
            _controller.NewGame();
            _random.NextValue = 1f;
            OpenChest(0);
            Assert.AreEqual(1, _controller.Attempts, "guard: the first chest should have consumed an attempt");

            List<int> attempts = new();
            _controller.OnAttemptsChanged += attempts.Add;
            _controller.NewGame();

            Assert.AreEqual(0, _controller.Attempts);
            CollectionAssert.AreEqual(new[] { 0 }, attempts);
        }

        [Test]
        public void NewGame_ReclosesChestsOpenedInThePreviousRound()
        {
            Inject();
            _controller.NewGame();
            _random.NextValue = 1f;
            OpenChest(0);

            _controller.NewGame();

            Assert.IsTrue(_controller.Chests.All(c => c.CurrentState == ChestsMinigameChestModel.State.Closed));
        }

        // --- The opening flow --------------------------------------------------------------

        [Test]
        public void ClickingAChest_LeavesItOpeningUntilTheTimerElapses()
        {
            Inject();
            _controller.NewGame();
            _random.NextValue = 1f;

            _controller.OnChestClicked(_controller.Chests[0]);

            Assert.AreEqual(ChestsMinigameChestModel.State.Opening, StateOf(0), "the chest starts opening immediately");
            Assert.AreEqual(0, _controller.Attempts, "the attempt is only spent once the chest finishes");

            _clock.AdvanceFrame();
            Assert.AreEqual(ChestsMinigameChestModel.State.Opening, StateOf(0), "still mid-flight after one of two frames");

            _clock.AdvanceFrame();
            Assert.AreEqual(ChestsMinigameChestModel.State.Open_Empty, StateOf(0));
            Assert.AreEqual(1, _controller.Attempts);
        }

        [Test]
        public void WhileOpening_ProgressAdvancesTowardsCompletion()
        {
            Inject();
            _controller.NewGame();
            _random.NextValue = 1f;

            _controller.OnChestClicked(_controller.Chests[0]);
            Assert.AreEqual(0f, _controller.Chests[0].Completition);

            _clock.AdvanceFrame();

            // One 50ms frame into a 100ms open.
            Assert.AreEqual(0.5f, _controller.Chests[0].Completition, 0.0001f);
            Assert.AreEqual(ChestsMinigameChestModel.State.Opening, StateOf(0));
        }

        [Test]
        public void ClickingASecondChest_CancelsTheFirstAndReclosesIt()
        {
            Inject();
            _controller.NewGame();
            _random.NextValue = 1f;

            _controller.OnChestClicked(_controller.Chests[0]);
            _clock.AdvanceFrame();
            _controller.OnChestClicked(_controller.Chests[1]);

            Assert.AreEqual(ChestsMinigameChestModel.State.Closed, StateOf(0), "the abandoned chest goes back to closed");

            _clock.AdvanceFrames(FramesToOpen);

            Assert.AreEqual(ChestsMinigameChestModel.State.Closed, StateOf(0));
            Assert.AreEqual(ChestsMinigameChestModel.State.Open_Empty, StateOf(1));
            Assert.AreEqual(1, _controller.Attempts, "only the chest that finished costs an attempt");
        }

        [Test]
        public void CancellingAnOpeningChest_LeavesNoWorkRunning()
        {
            // The abandoned chest's two tasks must actually unwind, not linger and fire later.
            Inject();
            _controller.NewGame();
            _random.NextValue = 1f;

            _controller.OnChestClicked(_controller.Chests[0]);
            _clock.AdvanceFrame();
            _controller.NewGame();

            _clock.AdvanceUntilIdle();

            Assert.AreEqual(0, _clock.PendingWaiters);
            Assert.AreEqual(0, _controller.Attempts, "a cancelled chest never reaches OpenChest");
            Assert.IsTrue(_controller.Chests.All(c => c.CurrentState == ChestsMinigameChestModel.State.Closed));
        }

        [Test]
        public void ReclickingTheChestAlreadyOpening_IsIgnoredAndItsTimerKeepsRunning()
        {
            Inject();
            _controller.NewGame();
            _random.NextValue = 1f;

            _controller.OnChestClicked(_controller.Chests[0]);
            _clock.AdvanceFrame();
            float progressSoFar = _controller.Chests[0].Completition;

            // The chest is Opening rather than Closed, so OnChestClicked rejects the second tap.
            // An impatient double-tap must not restart the timer or queue a second open.
            _controller.OnChestClicked(_controller.Chests[0]);

            Assert.AreEqual(ChestsMinigameChestModel.State.Opening, StateOf(0));
            Assert.AreEqual(progressSoFar, _controller.Chests[0].Completition, "progress carried on rather than resetting");

            _clock.AdvanceFrame();

            Assert.AreEqual(ChestsMinigameChestModel.State.Open_Empty, StateOf(0), "the original timer finished on schedule");
            Assert.AreEqual(1, _controller.Attempts, "the double-tap did not cost a second attempt");
        }

        // The progress loop and the open timer come due in the same frame, and nothing in the
        // engine promises which resumes first. Rather than assume an answer, run the flow under
        // both orderings and require the outcome to be identical.
        [TestCase(true, TestName = "OpeningIsUnaffectedByScheduling_WhenProgressResumesFirst")]
        [TestCase(false, TestName = "OpeningIsUnaffectedByScheduling_WhenTheTimerResumesFirst")]
        public void OpeningIsUnaffectedByScheduling(bool frameWaitersResumeFirst)
        {
            _clock.FrameWaitersResumeFirst = frameWaitersResumeFirst;
            Inject();
            _controller.NewGame();
            _random.NextValue = 1f;

            List<ChestsMinigameChestModel.State> sequence = new();
            _controller.Chests[0].OnStateChanged += sequence.Add;

            OpenChest(0);

            Assert.AreEqual(ChestsMinigameChestModel.State.Open_Empty, StateOf(0));
            Assert.AreEqual(1, _controller.Attempts);
            Assert.AreEqual(1f, _controller.Chests[0].Completition, "a settled chest reads as fully open");

            int openedAt = sequence.FindIndex(state =>
                state is ChestsMinigameChestModel.State.Open_Empty or ChestsMinigameChestModel.State.Open_Prize);
            Assert.GreaterOrEqual(openedAt, 0);
            CollectionAssert.DoesNotContain(sequence.GetRange(openedAt, sequence.Count - openedAt),
                ChestsMinigameChestModel.State.Opening,
                "an opened chest must never report Opening again, whichever task resumes first");
        }

        // --- Attempt accounting ------------------------------------------------------------

        [Test]
        public void EachCompletedChest_ConsumesAnAttemptAndAnnouncesTheNewCount()
        {
            Inject();
            _controller.NewGame();
            _random.NextValue = 1f;
            List<int> attempts = new();
            _controller.OnAttemptsChanged += attempts.Add;

            OpenChest(0);
            OpenChest(1);

            Assert.AreEqual(2, _controller.Attempts);
            CollectionAssert.AreEqual(new[] { 1, 2 }, attempts);
        }

        // --- Prize resolution --------------------------------------------------------------

        [Test]
        public void PrizeChance_IsOneOverTheChestsStillUnopened()
        {
            // One prize among 4 chests: the odds run 1/4, 1/3, 1/2, then 1/1. Each draw sits just
            // above its threshold until the final chest, which holds the prize by elimination.
            Inject();
            _controller.NewGame();
            _random.ValueSequence.Enqueue(0.26f);  // > 1/4 -> empty
            _random.ValueSequence.Enqueue(0.34f);  // > 1/3 -> empty
            _random.ValueSequence.Enqueue(0.51f);  // > 1/2 -> empty
            _random.ValueSequence.Enqueue(0.99f);  // <= 1/1 -> prize

            for (int i = 0; i < 4; i++)
            {
                OpenChest(i);
            }

            Assert.AreEqual(ChestsMinigameChestModel.State.Open_Empty, StateOf(0));
            Assert.AreEqual(ChestsMinigameChestModel.State.Open_Empty, StateOf(1));
            Assert.AreEqual(ChestsMinigameChestModel.State.Open_Empty, StateOf(2));
            Assert.AreEqual(ChestsMinigameChestModel.State.Open_Prize, StateOf(3));
        }

        [Test]
        public void WithTheUnluckiestDraws_ThePrizeWaitsInTheFinalChest()
        {
            // The prize has to be somewhere, so a player who keeps missing still finds it in the
            // last chest rather than one chest early. This is the regression guard for the divisor
            // in TryGiveChestPrize: drop its +1 and the win lands on chest N-1 instead.
            Inject();
            _controller.NewGame();
            _random.NextValue = 1f; // the unluckiest possible draw, every time

            bool? outcome = null;
            _controller.OnGameFinished += won => outcome = won;

            for (int i = 0; i < 3; i++)
            {
                OpenChest(i);
                Assert.IsNull(outcome, $"chest {i} should still be empty at 1/{4 - i} odds");
            }
            OpenChest(3);

            Assert.AreEqual(true, outcome);
            Assert.AreEqual(4, _controller.Attempts);
            Assert.AreEqual(ChestsMinigameChestModel.State.Open_Prize, StateOf(3));
        }

        [Test]
        public void EveryChest_CanHoldThePrize()
        {
            // The counterpart to the test above: no position is excluded from winning. Each run
            // misses on every earlier chest, then draws 0 on the target one.
            for (int target = 0; target < 4; target++)
            {
                SetUp();
                Inject();
                _controller.NewGame();

                for (int i = 0; i < target; i++)
                {
                    _random.ValueSequence.Enqueue(1f);
                }
                _random.ValueSequence.Enqueue(0f);

                for (int i = 0; i <= target; i++)
                {
                    OpenChest(i);
                }

                Assert.AreEqual(ChestsMinigameChestModel.State.Open_Prize, StateOf(target),
                    $"chest {target} must be able to win");
            }
        }

        // --- End of game -------------------------------------------------------------------

        [Test]
        public void WinningAChest_EndsTheGameAndRequestsAReward()
        {
            Inject();
            _controller.NewGame();
            _random.NextValue = 0f;
            bool? outcome = null;
            _controller.OnGameFinished += won => outcome = won;

            OpenChest(0);

            Assert.AreEqual(true, outcome);
            Assert.AreEqual(ChestsMinigameController.State.Ended, _controller.CurrentState);
            CollectionAssert.AreEqual(new[] { "ChestsMinigame" }, _rewards.GiveRewardCalls);
        }

        [Test]
        public void RunningOutOfAttempts_EndsTheGameWithoutAReward()
        {
            // Attempts must be scarcer than chests for a loss to be reachable at all.
            _config.ChestCount = 10;
            _config.AttempsCount = 2;
            Inject();
            _controller.NewGame();
            _random.NextValue = 1f;
            bool? outcome = null;
            _controller.OnGameFinished += won => outcome = won;

            OpenChest(0);
            OpenChest(1);

            Assert.AreEqual(false, outcome);
            Assert.AreEqual(ChestsMinigameController.State.Ended, _controller.CurrentState);
            CollectionAssert.IsEmpty(_rewards.GiveRewardCalls);
        }

        // --- Input guards ------------------------------------------------------------------

        [Test]
        public void OnChestClicked_BeforeTheGameStarts_IsIgnored()
        {
            Inject();

            _controller.OnChestClicked(_controller.Chests[0]);
            _clock.AdvanceFrames(FramesToOpen);

            Assert.AreEqual(ChestsMinigameChestModel.State.Closed, StateOf(0));
            Assert.AreEqual(0, _controller.Attempts);
        }

        [Test]
        public void OnChestClicked_OnAnAlreadyOpenChest_IsIgnored()
        {
            Inject();
            _controller.NewGame();
            _random.NextValue = 1f;
            OpenChest(0);

            _controller.OnChestClicked(_controller.Chests[0]);
            _clock.AdvanceFrames(FramesToOpen);

            Assert.AreEqual(ChestsMinigameChestModel.State.Open_Empty, StateOf(0));
            Assert.AreEqual(1, _controller.Attempts);
        }

        [Test]
        public void OnceTheGameEnds_FurtherClicksAreIgnored()
        {
            Inject();
            _controller.NewGame();
            _random.NextValue = 0f;
            OpenChest(0);
            Assert.AreEqual(ChestsMinigameController.State.Ended, _controller.CurrentState);

            _controller.OnChestClicked(_controller.Chests[1]);
            _clock.AdvanceFrames(FramesToOpen);

            Assert.AreEqual(ChestsMinigameChestModel.State.Closed, StateOf(1));
            Assert.AreEqual(1, _controller.Attempts);
        }

        // --- Disposal ----------------------------------------------------------------------

        [Test]
        public void Dispose_DropsEveryEventSubscriber()
        {
            Inject();
            bool anyHandlerRan = false;
            _controller.OnStateChange += _ => anyHandlerRan = true;
            _controller.OnAttemptsChanged += _ => anyHandlerRan = true;
            _controller.OnGameFinished += _ => anyHandlerRan = true;

            _controller.Dispose();
            _controller.NewGame();

            Assert.IsFalse(anyHandlerRan, "the view stays subscribed to all three events, so all three must be cleared");
        }

        [Test]
        public void Dispose_CancelsAChestThatWasStillOpening()
        {
            Inject();
            _controller.NewGame();
            _random.NextValue = 1f;
            _controller.OnChestClicked(_controller.Chests[0]);
            _clock.AdvanceFrame();

            _controller.Dispose();
            _clock.AdvanceUntilIdle();

            Assert.AreEqual(0, _clock.PendingWaiters, "disposal must not leave work parked on the clock");
            Assert.AreEqual(0, _controller.Attempts);
        }
    }
}
