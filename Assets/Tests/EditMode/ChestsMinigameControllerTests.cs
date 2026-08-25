using System.Collections.Generic;
using System.Linq;
using Company.ChestGame.Minigame.Chests;
using Company.ChestGame.Minigame.Chests.Internal;
using Company.ChestGame.Tests.Common;
using NUnit.Framework;

namespace Company.ChestGame.Tests.EditMode
{
    // Everything here goes through the real entry point, OnChestClicked, including the two
    // concurrent UniTasks it spawns. FakeGameClock is what makes that possible in edit mode.
    // Timings are chosen so a chest takes exactly two frames to open: a 100ms open at 50ms per
    // frame.
    public class ChestsMinigameControllerTests
    {
        private const int OpenMilliseconds = 100;
        private const int FramesToOpen = 2;

        private FakeRewardsManager _rewards;
        private FakeRandomProvider _random;
        private FakeGameClock _clock;
        private ChestsMinigameController _controller;

        [SetUp]
        public void SetUp()
        {
            _rewards = new FakeRewardsManager();
            _random = new FakeRandomProvider();
            _clock = new FakeGameClock { DeltaTime = 0.05f };
            _controller = new ChestsMinigameController();
        }

        [TearDown]
        public void TearDown() => _controller.Dispose();

        // Mirrors the framework's own order: ChestsMinigameSO configures the controller,
        // MinigameManager.Get injects it afterwards. The real config type rather than a fake,
        // because it is a plain validated value.
        private void ConfigureAndInject(int chestCount = 4, int attemptsCount = 4)
        {
            _controller.Configure(ChestsMinigameConfig.Create(chestCount, attemptsCount, OpenMilliseconds));
            _controller.Inject(_rewards, _random, _clock);
        }

        // Clicks a chest and lets it run all the way to open.
        private void OpenChest(int index)
        {
            _controller.OnChestClicked(_controller.Chests[index]);
            _clock.AdvanceFrames(FramesToOpen);
        }

        private ChestsMinigameChestModel.State StateOf(int index) => _controller.Chests[index].CurrentState;

        // --- Configuration -----------------------------------------------------------------

        [Test]
        public void Configure_BuildsOneChestPerConfiguredChestCount()
        {
            ConfigureAndInject(chestCount: 7);

            Assert.AreEqual(7, _controller.Chests.Count);
            Assert.IsTrue(_controller.Chests.All(c => c.CurrentState == ChestsMinigameChestModel.State.Closed));
        }

        [Test]
        public void Configure_TakesTotalAttemptsFromConfig_AndStartsIdle()
        {
            ConfigureAndInject(attemptsCount: 5);

            Assert.AreEqual(5, _controller.TotalAttempts);
            Assert.AreEqual(0, _controller.Attempts);
            Assert.AreEqual(ChestsMinigameController.State.NotStarted, _controller.CurrentState);
        }

        // --- New game ----------------------------------------------------------------------

        [Test]
        public void NewGame_EntersPlayingAndAnnouncesIt()
        {
            ConfigureAndInject();
            List<ChestsMinigameController.State> states = new();
            _controller.OnStateChange += states.Add;

            _controller.NewGame();

            Assert.AreEqual(ChestsMinigameController.State.Playing, _controller.CurrentState);
            CollectionAssert.AreEqual(new[] { ChestsMinigameController.State.Playing }, states);
        }

        [Test]
        public void NewGame_ResetsAttemptsAndAnnouncesTheNewCount()
        {
            ConfigureAndInject();
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
            ConfigureAndInject();
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
            ConfigureAndInject();
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
            ConfigureAndInject();
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
            ConfigureAndInject();
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
            // The abandoned chest's two tasks must unwind, not linger and fire later.
            ConfigureAndInject();
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
            ConfigureAndInject();
            _controller.NewGame();
            _random.NextValue = 1f;

            _controller.OnChestClicked(_controller.Chests[0]);
            _clock.AdvanceFrame();
            float progressSoFar = _controller.Chests[0].Completition;

            // The chest is Opening rather than Closed, so an impatient double-tap must not restart
            // the timer or queue a second open.
            _controller.OnChestClicked(_controller.Chests[0]);

            Assert.AreEqual(ChestsMinigameChestModel.State.Opening, StateOf(0));
            Assert.AreEqual(progressSoFar, _controller.Chests[0].Completition, "progress carried on rather than resetting");

            _clock.AdvanceFrame();

            Assert.AreEqual(ChestsMinigameChestModel.State.Open_Empty, StateOf(0), "the original timer finished on schedule");
            Assert.AreEqual(1, _controller.Attempts, "the double-tap did not cost a second attempt");
        }

        // The progress loop and the open timer come due in the same frame and nothing promises
        // which resumes first, so the flow runs under both orderings.
        [TestCase(true, TestName = "OpeningIsUnaffectedByScheduling_WhenProgressResumesFirst")]
        [TestCase(false, TestName = "OpeningIsUnaffectedByScheduling_WhenTheTimerResumesFirst")]
        public void OpeningIsUnaffectedByScheduling(bool frameWaitersResumeFirst)
        {
            _clock.FrameWaitersResumeFirst = frameWaitersResumeFirst;
            ConfigureAndInject();
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
            ConfigureAndInject();
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
            ConfigureAndInject();
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
            // The prize has to be somewhere. Regression guard for the divisor in TryGiveChestPrize:
            // drop its +1 and the win lands on chest N-1 instead.
            ConfigureAndInject();
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
            // The counterpart: no position is excluded from winning.
            for (int target = 0; target < 4; target++)
            {
                SetUp();
                ConfigureAndInject();
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
            ConfigureAndInject();
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
            ConfigureAndInject(chestCount: 10, attemptsCount: 2);
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
            ConfigureAndInject();

            _controller.OnChestClicked(_controller.Chests[0]);
            _clock.AdvanceFrames(FramesToOpen);

            Assert.AreEqual(ChestsMinigameChestModel.State.Closed, StateOf(0));
            Assert.AreEqual(0, _controller.Attempts);
        }

        [Test]
        public void OnChestClicked_OnAnAlreadyOpenChest_IsIgnored()
        {
            ConfigureAndInject();
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
            ConfigureAndInject();
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
            ConfigureAndInject();
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
            ConfigureAndInject();
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
