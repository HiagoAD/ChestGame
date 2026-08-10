using System.Collections.Generic;
using Company.ChestGame.Minigame.Chests.Internal;
using NUnit.Framework;

namespace Company.ChestGame.Tests.EditMode
{
    // ChestsMinigameChestModel is plain C# with no Unity dependency, so its whole state machine is
    // reachable from edit mode.
    public class ChestsMinigameChestModelTests
    {
        private ChestsMinigameChestModel _chest;
        private List<ChestsMinigameChestModel.State> _observedStates;

        [SetUp]
        public void SetUp()
        {
            _chest = new ChestsMinigameChestModel();
            _observedStates = new List<ChestsMinigameChestModel.State>();
            _chest.OnStateChanged += state => _observedStates.Add(state);
        }

        [Test]
        public void NewChest_IsClosedWithNoProgress()
        {
            Assert.AreEqual(ChestsMinigameChestModel.State.Closed, _chest.CurrentState);
            Assert.AreEqual(0f, _chest.Completition);
        }

        [Test]
        public void SetClosed_WhenAlreadyClosed_RaisesNoEvent()
        {
            _chest.SetClosed();

            CollectionAssert.IsEmpty(_observedStates);
        }

        [Test]
        public void SetOpening_StoresProgressAndRaisesEvent()
        {
            _chest.SetOpening(0.25f);

            Assert.AreEqual(ChestsMinigameChestModel.State.Opening, _chest.CurrentState);
            Assert.AreEqual(0.25f, _chest.Completition);
            CollectionAssert.AreEqual(new[] { ChestsMinigameChestModel.State.Opening }, _observedStates);
        }

        [Test]
        public void SetOpening_RaisesEveryTime_SoProgressTicksReachListeners()
        {
            _chest.SetOpening(0.1f);
            _chest.SetOpening(0.2f);
            _chest.SetOpening(0.3f);

            Assert.AreEqual(3, _observedStates.Count);
            Assert.AreEqual(0.3f, _chest.Completition);
        }

        [Test]
        public void StateChangedEvent_SeesTheUpdatedProgress()
        {
            // Completition is written before the state property raises, so a listener that reads
            // both never observes a half-applied chest.
            float progressSeenByListener = -1f;
            _chest.OnStateChanged += _ => progressSeenByListener = _chest.Completition;

            _chest.SetOpening(0.75f);

            Assert.AreEqual(0.75f, progressSeenByListener);
        }

        [Test]
        public void SetOpen_WithPrize_BecomesOpenPrizeAndCompletes()
        {
            _chest.SetOpen(true);

            Assert.AreEqual(ChestsMinigameChestModel.State.Open_Prize, _chest.CurrentState);
            Assert.AreEqual(1f, _chest.Completition);
        }

        [Test]
        public void SetOpen_WithoutPrize_BecomesOpenEmptyAndCompletes()
        {
            _chest.SetOpen(false);

            Assert.AreEqual(ChestsMinigameChestModel.State.Open_Empty, _chest.CurrentState);
            Assert.AreEqual(1f, _chest.Completition);
        }

        [Test]
        public void SetOpen_WhenAlreadyOpen_IsIgnored()
        {
            _chest.SetOpen(true);
            _observedStates.Clear();

            _chest.SetOpen(false);

            Assert.AreEqual(ChestsMinigameChestModel.State.Open_Prize, _chest.CurrentState);
            CollectionAssert.IsEmpty(_observedStates);
        }

        [Test]
        public void SetOpening_AfterTheChestIsOpen_IsIgnored()
        {
            // The progress loop and the open timer resume in the same frame. If a stray progress
            // tick ever arrived after the chest opened, an opened chest would visibly reopen.
            _chest.SetOpen(true);
            _observedStates.Clear();

            _chest.SetOpening(0.3f);

            Assert.AreEqual(ChestsMinigameChestModel.State.Open_Prize, _chest.CurrentState);
            Assert.AreEqual(1f, _chest.Completition, "progress stays complete");
            CollectionAssert.IsEmpty(_observedStates);
        }

        [Test]
        public void SetClosed_AfterOpening_ResetsProgress()
        {
            _chest.SetOpening(0.5f);

            _chest.SetClosed();

            Assert.AreEqual(ChestsMinigameChestModel.State.Closed, _chest.CurrentState);
            Assert.AreEqual(0f, _chest.Completition);
        }
    }
}
