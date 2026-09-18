using System.Collections.Generic;
using System.Text.RegularExpressions;
using Company.ChestGame.Common;
using Company.ChestGame.Pooling;
using Company.ChestGame.Pooling.Demo;
using Company.ChestGame.Tests.Common;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Company.ChestGame.Tests.EditMode
{
    /// <summary>
    /// <see cref="PoolRace{T}"/>'s orchestration: one <see cref="FrameBudgetedLoop"/> per lane, all
    /// reading the same clock and budget, started together and cancelled together.
    /// </summary>
    /// <remarks>
    /// See docs/pooling.md, "How the race is measured".
    /// </remarks>
    public class PoolRaceTests
    {
        private const double BudgetMilliseconds = 10d;

        private readonly List<Object> _created = new();
        private FakeGameClock _clock;

        [SetUp]
        public void SetUp()
        {
            _clock = new FakeGameClock();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object created in _created)
            {
                if (created != null) Object.DestroyImmediate(created);
            }
            _created.Clear();
        }

        private RectTransform NewRect(string name)
        {
            GameObject go = new(name, typeof(RectTransform));
            _created.Add(go);
            return (RectTransform)go.transform;
        }

        private PoolRaceLane<RectTransform> FakeLane(PoolStrategy strategy, double costPerGetMilliseconds) =>
            new(strategy,
                new FakePrefabPool<RectTransform>(_clock, costPerGetMilliseconds, () => NewRect("Instance")),
                NewRect($"{strategy}Fill"));

        /// <remarks>
        /// See docs/pooling.md, "How the race is measured".
        /// </remarks>
        [Test]
        public void StartRace_EveryLaneAdvancesInTheSameFrames()
        {
            PoolRaceLane<RectTransform>[] lanes =
            {
                FakeLane(PoolStrategy.ActivationPool, 4d),
                FakeLane(PoolStrategy.ParkedPool, 4d),
                FakeLane(PoolStrategy.UnityPool, 4d),
                FakeLane(PoolStrategy.DirectSpawner, 4d)
            };

            PoolRace<RectTransform> race = new(lanes, _clock, BudgetMilliseconds);
            race.StartRace(9, FillMode.Cold, solo: false, PoolStrategy.ActivationPool);

            foreach (PoolRaceLane<RectTransform> lane in lanes)
            {
                Assert.AreEqual(3, lane.Pool.ActiveCount, $"{lane.Strategy} should have placed its first frame's worth");
            }

            _clock.AdvanceFrame();

            foreach (PoolRaceLane<RectTransform> lane in lanes)
            {
                Assert.AreEqual(6, lane.Pool.ActiveCount,
                    $"{lane.Strategy} did not advance on the same frame as the others - a lane driven off its own schedule instead of the shared clock would fall behind or race ahead here");
            }
        }

        [Test]
        public void StartRace_ACheaperLanePlacesMoreItemsPerFrame_ThanAnExpensiveOneAtTheSameBudget()
        {
            PoolRaceLane<RectTransform> cheap = FakeLane(PoolStrategy.ActivationPool, 1d);
            PoolRaceLane<RectTransform> expensive = FakeLane(PoolStrategy.ParkedPool, 5d);

            PoolRace<RectTransform> race = new(new[] { cheap, expensive }, _clock, BudgetMilliseconds);
            race.StartRace(20, FillMode.Cold, solo: false, PoolStrategy.ActivationPool);

            Assert.AreEqual(10, cheap.Pool.ActiveCount, "ten one-millisecond units fit inside a ten millisecond budget");
            Assert.AreEqual(2, expensive.Pool.ActiveCount, "two five-millisecond ones fill it");
            Assert.Greater(cheap.Pool.ActiveCount, expensive.Pool.ActiveCount,
                "if the cheaper lane does not visibly get further in the same frame, the race is counting items per lane rather than budgeting time, and the whole demonstration shows nothing");
        }

        /// <remarks>
        /// See docs/pooling.md, "How the race is measured".
        /// </remarks>
        [Test]
        public void StartRace_ACheaperLaneReportsLessElapsedTime_ThanAnExpensiveOneAtTheSameBudget()
        {
            PoolRaceLane<RectTransform> cheap = FakeLane(PoolStrategy.ActivationPool, 1d);
            PoolRaceLane<RectTransform> expensive = FakeLane(PoolStrategy.ParkedPool, 5d);

            PoolRace<RectTransform> race = new(new[] { cheap, expensive }, _clock, BudgetMilliseconds);
            race.StartRace(20, FillMode.Cold, solo: false, PoolStrategy.ActivationPool);
            _clock.AdvanceUntilIdle();

            Assert.IsTrue(race.LastResult.HasValue, "guard: the race has to have settled to read its result");

            LaneMetrics cheapMetrics = MetricsFor(race.LastResult.Value, PoolStrategy.ActivationPool);
            LaneMetrics expensiveMetrics = MetricsFor(race.LastResult.Value, PoolStrategy.ParkedPool);

            Assert.Less(cheapMetrics.ElapsedMilliseconds, expensiveMetrics.ElapsedMilliseconds,
                "the cheap lane finished placing its whole board in fewer frames than the expensive one, so it has to " +
                "report less elapsed clock time - equal numbers here mean the finish time was read after the whole " +
                "race settled instead of after each lane's own fill");
        }

        private static LaneMetrics MetricsFor(RaceResult result, PoolStrategy strategy)
        {
            foreach (LaneMetrics metrics in result.Lanes)
            {
                if (metrics.Strategy == strategy) return metrics;
            }

            Assert.Fail($"no lane metrics for {strategy}");
            return default;
        }

        [Test]
        public void CancelRace_StopsEveryLane_NotJustTheFirst()
        {
            PoolRaceLane<RectTransform>[] lanes =
            {
                FakeLane(PoolStrategy.ActivationPool, 4d),
                FakeLane(PoolStrategy.ParkedPool, 4d),
                FakeLane(PoolStrategy.UnityPool, 4d),
                FakeLane(PoolStrategy.DirectSpawner, 4d)
            };

            PoolRace<RectTransform> race = new(lanes, _clock, BudgetMilliseconds);
            race.StartRace(9, FillMode.Cold, solo: false, PoolStrategy.ActivationPool);

            foreach (PoolRaceLane<RectTransform> lane in lanes)
            {
                Assert.AreEqual(3, lane.Pool.ActiveCount, "guard: the first frame's worth");
            }

            race.CancelRace();
            _clock.AdvanceFrames(5);

            foreach (PoolRaceLane<RectTransform> lane in lanes)
            {
                Assert.AreEqual(3, lane.Pool.ActiveCount,
                    $"{lane.Strategy} kept placing after cancellation - a race that only threads its token through the first lane would leave exactly this one still running");
            }

            Assert.IsFalse(race.IsRunning);
            Assert.IsNull(race.LastResult, "a cancelled race never settles, so it must never publish a result either");
        }

        [Test]
        public void StartRace_EachLaneFinishesWithExactlyTheRequestedCount()
        {
            RectTransform prefab = NewRect("Prefab");
            const int boardSize = 12;

            Transform[] laneRoots =
            {
                NewRect("ActivationRoot"), NewRect("ParkedRoot"), NewRect("UnityPoolRoot"), NewRect("DirectRoot")
            };

            PoolRaceLane<RectTransform>[] lanes = PoolRaceLaneFactory.BuildAll(prefab, laneRoots, maxSize: 50);
            PoolRace<RectTransform> race = new(lanes, _clock, BudgetMilliseconds);

            race.StartRace(boardSize, FillMode.Cold, solo: false, PoolStrategy.ActivationPool);
            _clock.AdvanceUntilIdle();

            foreach (PoolRaceLane<RectTransform> lane in lanes)
            {
                Assert.AreEqual(boardSize, lane.Pool.ActiveCount, $"{lane.Strategy} did not finish with the full board");
            }
        }

        /// <remarks>
        /// See docs/pooling.md, "How the race is measured".
        /// </remarks>
        [Test]
        public void StartRace_Prewarmed_InstantiatesNothingDuringTheRace()
        {
            RectTransform prefab = NewRect("Prefab");
            const int boardSize = 10;

            PoolRaceLane<RectTransform>[] lanes =
            {
                PoolRaceLaneFactory.Build(PoolStrategy.ActivationPool, prefab, NewRect("ActivationRoot"), 50),
                PoolRaceLaneFactory.Build(PoolStrategy.ParkedPool, prefab, NewRect("ParkedRoot"), 50),
                PoolRaceLaneFactory.Build(PoolStrategy.UnityPool, prefab, NewRect("UnityPoolRoot"), 50)
            };

            PoolRace<RectTransform> race = new(lanes, _clock, BudgetMilliseconds);
            race.StartRace(boardSize, FillMode.Prewarmed, solo: false, PoolStrategy.ActivationPool);
            _clock.AdvanceUntilIdle();

            Assert.IsTrue(race.LastResult.HasValue, "guard: the race has to have settled to read its result");
            foreach (LaneMetrics lane in race.LastResult.Value.Lanes)
            {
                Assert.AreEqual(boardSize, lane.PlacedCount, $"{lane.Strategy} did not place the full board");
                Assert.AreEqual(0, lane.Instantiated,
                    $"{lane.Strategy} instantiated during the timed race - prewarming is supposed to pay that cost before the clock starts, not during it");
            }
        }

        /// <remarks>
        /// See docs/pooling.md, "How the race is measured".
        /// </remarks>
        [Test]
        public void StartRace_Reuse_InstantiatesNothingOnPooledLanes_ButTheFullBoardOnTheBaseline()
        {
            RectTransform prefab = NewRect("Prefab");
            const int boardSize = 10;

            Transform[] laneRoots =
            {
                NewRect("ActivationRoot"), NewRect("ParkedRoot"), NewRect("UnityPoolRoot"), NewRect("DirectRoot")
            };

            PoolRaceLane<RectTransform>[] lanes = PoolRaceLaneFactory.BuildAll(prefab, laneRoots, maxSize: 50);
            PoolRace<RectTransform> race = new(lanes, _clock, BudgetMilliseconds);

            race.StartRace(boardSize, FillMode.Cold, solo: false, PoolStrategy.ActivationPool);
            _clock.AdvanceUntilIdle();
            Assert.IsTrue(race.LastResult.HasValue, "guard: the first race has to have settled");

            ExpectDestroys(boardSize);
            race.StartRace(boardSize, FillMode.Reuse, solo: false, PoolStrategy.ActivationPool);
            _clock.AdvanceUntilIdle();

            Assert.IsTrue(race.LastResult.HasValue, "guard: the reuse race has to have settled");
            foreach (LaneMetrics lane in race.LastResult.Value.Lanes)
            {
                Assert.AreEqual(boardSize, lane.PlacedCount, $"{lane.Strategy} did not place the full board");

                if (lane.Strategy == PoolStrategy.DirectSpawner)
                {
                    Assert.AreEqual(boardSize, lane.Instantiated,
                        "DirectSpawner has nowhere to hold what a previous race released, so a reuse race still has to instantiate the whole board");
                }
                else
                {
                    Assert.AreEqual(0, lane.Instantiated,
                        $"{lane.Strategy} instantiated on a reuse race - it should have found the previous race's board still parked and waiting");
                }
            }
        }

        [Test]
        public void NewRace_SelectionDefaults_MatchWhatTheDemoOpensWith()
        {
            PoolRace<RectTransform> race = new(new[] { FakeLane(PoolStrategy.ActivationPool, 1d) }, _clock, BudgetMilliseconds);

            Assert.AreEqual(1, race.BoardSizeIndex);
            Assert.AreEqual(FillMode.Cold, race.FillMode);
            Assert.IsFalse(race.Solo);
            Assert.AreEqual(PoolStrategy.ActivationPool, race.SoloStrategy);
        }

        [Test]
        public void BoardSizes_LargestValue_EqualsMaxBoardSize()
        {
            PoolRace<RectTransform> race = new(new[] { FakeLane(PoolStrategy.ActivationPool, 1d) }, _clock, BudgetMilliseconds);

            Assert.AreEqual(PoolRace<RectTransform>.MaxBoardSize, race.BoardSizes[race.BoardSizes.Count - 1],
                "the largest selectable board size has to be exactly what every lane's pool was built with room for, or the biggest selection could ask a lane to hold more than it has space for");
        }

        [Test]
        public void SetBoardSize_ChangesBoardSizeIndex_AndRaisesOnSelectionChanged()
        {
            PoolRace<RectTransform> race = new(new[] { FakeLane(PoolStrategy.ActivationPool, 1d) }, _clock, BudgetMilliseconds);
            int raised = 0;
            race.OnSelectionChanged += () => raised++;

            race.SetBoardSize(2);

            Assert.AreEqual(2, race.BoardSizeIndex);
            Assert.AreEqual(1, raised);
        }

        [Test]
        public void CycleFillMode_GoesColdThenPrewarmedThenReuseThenBackToCold()
        {
            PoolRace<RectTransform> race = new(new[] { FakeLane(PoolStrategy.ActivationPool, 1d) }, _clock, BudgetMilliseconds);

            race.CycleFillMode();
            Assert.AreEqual(FillMode.Prewarmed, race.FillMode);

            race.CycleFillMode();
            Assert.AreEqual(FillMode.Reuse, race.FillMode);

            race.CycleFillMode();
            Assert.AreEqual(FillMode.Cold, race.FillMode);
        }

        [Test]
        public void ToggleSolo_FlipsSolo_AndRaisesOnSelectionChanged()
        {
            PoolRace<RectTransform> race = new(new[] { FakeLane(PoolStrategy.ActivationPool, 1d) }, _clock, BudgetMilliseconds);
            int raised = 0;
            race.OnSelectionChanged += () => raised++;

            race.ToggleSolo();
            Assert.IsTrue(race.Solo);

            race.ToggleSolo();
            Assert.IsFalse(race.Solo);

            Assert.AreEqual(2, raised);
        }

        [Test]
        public void SetSoloStrategy_ChangesSoloStrategy_AndRaisesOnSelectionChanged()
        {
            PoolRaceLane<RectTransform>[] lanes = { FakeLane(PoolStrategy.ActivationPool, 1d), FakeLane(PoolStrategy.UnityPool, 1d) };
            PoolRace<RectTransform> race = new(lanes, _clock, BudgetMilliseconds);
            int raised = 0;
            race.OnSelectionChanged += () => raised++;

            race.SetSoloStrategy(PoolStrategy.UnityPool);

            Assert.AreEqual(PoolStrategy.UnityPool, race.SoloStrategy);
            Assert.AreEqual(1, raised);
        }

        [Test]
        public void StartRace_Parameterless_RunsTheCurrentlySelectedBoardSizeFillModeAndStrategy()
        {
            PoolRaceLane<RectTransform>[] lanes = { FakeLane(PoolStrategy.ActivationPool, 1d), FakeLane(PoolStrategy.ParkedPool, 1d) };
            PoolRace<RectTransform> race = new(lanes, _clock, BudgetMilliseconds);

            race.SetBoardSize(0);
            race.ToggleSolo();
            race.SetSoloStrategy(PoolStrategy.ParkedPool);

            race.StartRace();
            _clock.AdvanceUntilIdle();

            Assert.IsTrue(race.LastResult.HasValue, "guard: the race has to have settled");
            Assert.IsTrue(race.LastResult.Value.Solo, "the solo selection should have carried into the parameterless start");
            Assert.AreEqual(1, race.LastResult.Value.Lanes.Count, "solo carries exactly one lane");
            Assert.AreEqual(PoolStrategy.ParkedPool, race.LastResult.Value.Lanes[0].Strategy,
                "the selected solo strategy should have carried into the parameterless start");
            Assert.AreEqual(race.BoardSizes[0], race.LastResult.Value.Lanes[0].PlacedCount,
                "the selected board size should have carried into the parameterless start");
        }

        [Test]
        public void Tick_WhileRunning_TracksThePeakDeltaTime_AndClearsOnTheNextRace()
        {
            PoolRaceLane<RectTransform>[] lanes =
            {
                FakeLane(PoolStrategy.ActivationPool, 4d),
                FakeLane(PoolStrategy.ParkedPool, 4d)
            };
            PoolRace<RectTransform> race = new(lanes, _clock, BudgetMilliseconds);

            race.StartRace(9, FillMode.Cold, solo: false, PoolStrategy.ActivationPool);
            Assert.AreEqual(0f, race.PeakFrameSeconds, "guard: a freshly started race has nothing recorded yet");

            race.Tick(0.016f);
            race.Tick(0.033f);
            race.Tick(0.02f);

            Assert.AreEqual(0.033f, race.PeakFrameSeconds, 0.0001f,
                "the peak has to be the largest tick handed to it, not the last one or their sum");

            _clock.AdvanceUntilIdle();
            Assert.IsTrue(race.LastResult.HasValue, "guard: the race has to have settled");

            race.StartRace(9, FillMode.Cold, solo: false, PoolStrategy.ActivationPool);
            Assert.AreEqual(0f, race.PeakFrameSeconds, "starting a new race has to clear the previous race's peak");
        }

        [Test]
        public void Tick_WhileNotRunning_DoesNothing()
        {
            PoolRace<RectTransform> race = new(new[] { FakeLane(PoolStrategy.ActivationPool, 1d) }, _clock, BudgetMilliseconds);

            race.Tick(5f);

            Assert.AreEqual(0f, race.PeakFrameSeconds, "a race that never started has nothing to measure");
        }

        /// <summary>
        /// Expects exactly <paramref name="count"/> occurrences of the edit-mode destroy-refusal
        /// error log, matched by regex. Call before the action that triggers them.
        /// </summary>
        /// <remarks>
        /// See docs/pooling.md, "How the tests prove a release actually destroyed something".
        /// </remarks>
        private static void ExpectDestroys(int count)
        {
            for (int i = 0; i < count; i++) LogAssert.Expect(LogType.Error, EditModeDestroy);
        }

        private static readonly Regex EditModeDestroy = new("Destroy may not be called from edit mode");

        [Test]
        public void Constructing_WithoutLanesOrAClockOrABudget_ThrowsPoolRaceException()
        {
            PoolRaceLane<RectTransform>[] lanes = { FakeLane(PoolStrategy.ActivationPool, 1d) };

            Assert.Throws<PoolRaceException>(() => new PoolRace<RectTransform>(null, _clock, BudgetMilliseconds));
            Assert.Throws<PoolRaceException>(() => new PoolRace<RectTransform>(lanes, null, BudgetMilliseconds));
            Assert.Throws<PoolRaceException>(() => new PoolRace<RectTransform>(lanes, _clock, 0d));
        }

        [Test]
        public void StartRace_Solo_WithAStrategyThisRaceHasNoLaneFor_ThrowsPoolRaceException()
        {
            PoolRaceLane<RectTransform>[] lanes = { FakeLane(PoolStrategy.ActivationPool, 1d) };
            PoolRace<RectTransform> race = new(lanes, _clock, BudgetMilliseconds);

            Assert.Throws<PoolRaceException>(() => race.StartRace(5, FillMode.Cold, solo: true, PoolStrategy.ParkedPool));
        }

        [Test]
        public void PoolRaceException_IsDeliberatelyNotUnderChestGameException()
        {
            PoolRaceException failure = Assert.Throws<PoolRaceException>(
                () => new PoolRace<RectTransform>(null, _clock, BudgetMilliseconds));

            Assert.IsNotInstanceOf<ChestGameException>(failure,
                "or the shell would report a demo wired wrong to the player as a content download failure and carry on");
        }
    }
}
