using System;
using System.Collections.Generic;
using System.Threading;
using Company.ChestGame.Common;
using Company.ChestGame.Tests.Common;
using Cysharp.Threading.Tasks;
using NUnit.Framework;

namespace Company.ChestGame.Tests.EditMode
{
    /// <summary>
    /// Covers where <see cref="FrameBudgetedLoop"/> places each unit of work across frames, not the
    /// units themselves.
    /// </summary>
    /// <remarks>
    /// See docs/testing.md, "FrameBudgetedLoopTests, and what the tests are shaped around".
    /// </remarks>
    public class FrameBudgetedLoopTests
    {
        private const double BudgetMilliseconds = 10d;
        private const double CostPerUnitMilliseconds = 4d;
        private const int UnitsPerFrame = 3;
        private const int Units = 9;

        private FakeGameClock _clock;
        private List<int> _ran;

        [SetUp]
        public void SetUp()
        {
            _clock = new FakeGameClock();
            _ran = new List<int>();
        }

        private FrameBudgetedLoop Loop() => new(_clock, BudgetMilliseconds);

        private void CostlyStep(int index)
        {
            _ran.Add(index);
            _clock.Spend(CostPerUnitMilliseconds);
        }

        private void FreeStep(int index) => _ran.Add(index);

        [Test]
        public void RunAsync_RunsEveryUnitOnce_InOrder()
        {
            UniTask running = Loop().RunAsync(Units, CostlyStep, CancellationToken.None);

            _clock.AdvanceUntilIdle();

            SynchronousUniTask.Complete(running);
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8 }, _ran,
                "spreading the work over frames must not lose a unit, repeat one, or reorder them");
        }

        /// <remarks>
        /// See docs/testing.md, "FrameBudgetedLoopTests, and what the tests are shaped around".
        /// </remarks>
        [Test]
        public void RunAsync_SplitsTheWorkAcrossFrames_RatherThanDoingItAllInOne()
        {
            UniTask running = Loop().RunAsync(Units, CostlyStep, CancellationToken.None);

            Assert.AreEqual(UnitsPerFrame, _ran.Count,
                "all nine here is the single long frame this class exists to prevent; one is a loop yielding after every unit");

            _clock.AdvanceFrame();
            Assert.AreEqual(UnitsPerFrame * 2, _ran.Count);

            _clock.AdvanceFrame();
            Assert.AreEqual(Units, _ran.Count);
            SynchronousUniTask.Complete(running);
        }

        /// <remarks>
        /// See docs/testing.md, "FrameBudgetedLoopTests, and what the tests are shaped around".
        /// </remarks>
        [Test]
        public void RunAsync_PlacesMoreOfACheaperUnitInTheSameFrame()
        {
            List<int> cheap = new();
            FakeGameClock cheapClock = new();
            new FrameBudgetedLoop(cheapClock, BudgetMilliseconds)
                .RunAsync(20, index => { cheap.Add(index); cheapClock.Spend(1d); }, CancellationToken.None)
                .Forget();

            List<int> expensive = new();
            FakeGameClock expensiveClock = new();
            new FrameBudgetedLoop(expensiveClock, BudgetMilliseconds)
                .RunAsync(20, index => { expensive.Add(index); expensiveClock.Spend(5d); }, CancellationToken.None)
                .Forget();

            Assert.AreEqual(10, cheap.Count, "ten one-millisecond units fit inside a ten millisecond budget");
            Assert.AreEqual(2, expensive.Count, "two five-millisecond ones fill it");
            Assert.Greater(cheap.Count, expensive.Count,
                "if the cheaper work does not visibly get further in a frame, the budget is counting items and the comparison it is here to make shows nothing");
        }

        /// <remarks>
        /// See docs/architecture.md, "Why RunAsync is split, and the ordering inside the loop".
        /// </remarks>
        [Test]
        public void RunAsync_WhenOneUnitCostsMoreThanTheWholeBudget_StillPlacesOnePerFrame()
        {
            UniTask running = Loop().RunAsync(3, ExpensiveStep, CancellationToken.None);

            Assert.AreEqual(1, _ran.Count);

            _clock.AdvanceFrame();
            Assert.AreEqual(2, _ran.Count);

            _clock.AdvanceFrame();
            Assert.AreEqual(3, _ran.Count);
            SynchronousUniTask.Complete(running);
        }

        [Test]
        public void RunAsync_WithNothingToDo_FinishesWithoutCostingAFrame()
        {
            UniTask running = Loop().RunAsync(0, FreeStep, CancellationToken.None);

            SynchronousUniTask.Complete(running);
            CollectionAssert.IsEmpty(_ran);
            Assert.AreEqual(0, _clock.PendingWaiters,
                "an empty fill that parks a waiter has bought a frame to do nothing in");
        }

        [Test]
        public void RunAsync_WithOneUnitThatBlowsTheBudget_StillFinishesInThatFrame()
        {
            UniTask running = Loop().RunAsync(1, ExpensiveStep, CancellationToken.None);

            SynchronousUniTask.Complete(running);
            CollectionAssert.AreEqual(new[] { 0 }, _ran);
            Assert.AreEqual(0, _clock.PendingWaiters,
                "there was nothing left to place, so yielding would have bought a frame of nothing");
        }

        [Test]
        public void RunAsync_CancelledBetweenFrames_StopsWhereItGotTo()
        {
            using CancellationTokenSource cancellation = new();
            UniTask running = Loop().RunAsync(Units, CostlyStep, cancellation.Token);
            Assert.AreEqual(UnitsPerFrame, _ran.Count, "guard: the first frame's worth");

            cancellation.Cancel();
            _clock.AdvanceFrames(5);

            Assert.AreEqual(UnitsPerFrame, _ran.Count,
                "a cancelled fill that goes on placing is a fill running into a screen that has moved on");
            Assert.Throws<OperationCanceledException>(() => SynchronousUniTask.Complete(running),
                "and it has to report the cancellation rather than finish as though it had done the work");
        }

        [Test]
        public void RunAsync_WithATokenAlreadyCancelled_RunsNothingAtAll()
        {
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();

            UniTask running = Loop().RunAsync(Units, CostlyStep, cancellation.Token);

            CollectionAssert.IsEmpty(_ran,
                "the check has to come before the unit, or a fill that was cancelled before it began still places one");
            Assert.Throws<OperationCanceledException>(() => SynchronousUniTask.Complete(running));
        }

        [Test]
        public void Constructing_WithoutAClockOrWithoutABudget_ThrowsFrameBudgetException()
        {
            Assert.Throws<FrameBudgetException>(() => new FrameBudgetedLoop(null, BudgetMilliseconds));
            Assert.Throws<FrameBudgetException>(() => new FrameBudgetedLoop(_clock, 0d),
                "zero is one unit per frame, which is the shape the class exists to avoid, not a way to switch the budgeting off");
            Assert.Throws<FrameBudgetException>(() => new FrameBudgetedLoop(_clock, -1d));
        }

        /// <remarks>
        /// See docs/architecture.md, "Why RunAsync is split, and the ordering inside the loop".
        /// </remarks>
        [Test]
        public void RunAsync_WithNoUnitOrANegativeCount_ThrowsAtTheCallSiteRatherThanIntoTheTask()
        {
            Assert.Throws<FrameBudgetException>(() => Loop().RunAsync(1, null, CancellationToken.None));
            Assert.Throws<FrameBudgetException>(() => Loop().RunAsync(-1, FreeStep, CancellationToken.None));
            CollectionAssert.IsEmpty(_ran);
        }

        /// <remarks>
        /// See docs/architecture.md, "Exception hierarchy".
        /// </remarks>
        [Test]
        public void FrameBudgetException_IsDeliberatelyNotUnderChestGameException()
        {
            FrameBudgetException failure = Assert.Throws<FrameBudgetException>(
                () => new FrameBudgetedLoop(null, BudgetMilliseconds));

            Assert.IsNotInstanceOf<ChestGameException>(failure,
                "or the shell would report a wiring bug to the player as a content download failure and carry on");
        }

        private void ExpensiveStep(int index)
        {
            _ran.Add(index);
            _clock.Spend(BudgetMilliseconds * 10d);
        }
    }
}
