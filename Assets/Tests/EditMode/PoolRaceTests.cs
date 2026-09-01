using System.Collections.Generic;
using System.Text.RegularExpressions;
using Company.ChestGame.Common;
using Company.ChestGame.Pooling;
using Company.ChestGame.Pooling.Demo;
using Company.ChestGame.Tests.Common;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
// System brings a second Object with it; the alias keeps DestroyImmediate meaning what it did.
using Object = UnityEngine.Object;

namespace Company.ChestGame.Tests.EditMode
{
    // PoolRace<T>'s orchestration: one FrameBudgetedLoop per lane, all reading the same clock and
    // budget, started together and cancelled together. What is proven here is that shape, not what a
    // real pool costs - FakeGameClock cannot see a real engine, so the cost-sensitive tests race
    // FakePrefabPool lanes with a chosen cost, the way FrameBudgetedLoopTests races synthetic steps.
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

        // --- The shared clock drives every lane, not just the first ------------------------------

        [Test]
        public void StartRace_EveryLaneAdvancesInTheSameFrames()
        {
            // Same shape as FrameBudgetedLoopTests: four milliseconds a unit against a ten
            // millisecond budget places three a frame. All four lanes cost the same on purpose -
            // this is about whether one clock pumps every lane, not about which gets further.
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

        // --- The core claim of the whole phase ----------------------------------------------------

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

        // The same claim read off the metrics rather than the pool: a lane that finishes sooner has
        // to report a smaller elapsed time. Only true if each lane's finish is timestamped inside
        // its own task - stamping it after every lane has been awaited together would give them all
        // the slowest lane's finish time.
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

        // --- Cancellation ---------------------------------------------------------------------------

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

        // --- Finishing -------------------------------------------------------------------------------

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

        // --- Prewarming ------------------------------------------------------------------------------

        [Test]
        public void StartRace_Prewarmed_InstantiatesNothingDuringTheRace()
        {
            // The baseline is left out on purpose: DirectSpawner has nowhere to hold a prewarmed
            // instance, so it always instantiates on Get. That is correct behaviour for it.
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

        // --- Reuse -----------------------------------------------------------------------------------

        [Test]
        public void StartRace_Reuse_InstantiatesNothingOnPooledLanes_ButTheFullBoardOnTheBaseline()
        {
            // What Cold and Prewarmed cannot show: a second race finding what the first one placed
            // already parked, the way ChestsMinigameView's NewGame finds the board it released last
            // time. DirectSpawner has nowhere to have parked anything - its release is a real
            // destroy - so it is the one lane that pays the instantiate cost again.
            RectTransform prefab = NewRect("Prefab");
            const int boardSize = 10;

            Transform[] laneRoots =
            {
                NewRect("ActivationRoot"), NewRect("ParkedRoot"), NewRect("UnityPoolRoot"), NewRect("DirectRoot")
            };

            PoolRaceLane<RectTransform>[] lanes = PoolRaceLaneFactory.BuildAll(prefab, laneRoots, maxSize: 50);
            PoolRace<RectTransform> race = new(lanes, _clock, BudgetMilliseconds);

            // The first pass is what builds the stock a reuse race is supposed to find waiting.
            race.StartRace(boardSize, FillMode.Cold, solo: false, PoolStrategy.ActivationPool);
            _clock.AdvanceUntilIdle();
            Assert.IsTrue(race.LastResult.HasValue, "guard: the first race has to have settled");

            // Releasing the baseline's board before the second race starts destroys it for real in
            // edit mode - see PrefabPoolTests.ExpectDestroys for why this is pinned, not silenced.
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

        // Matched by regex rather than an exact message, for the reason PrefabPoolTests gives.
        // Declared before the act that causes it: a release the baseline is about to make is part
        // of the arrange, not a side effect to react to afterwards.
        private static void ExpectDestroys(int count)
        {
            for (int i = 0; i < count; i++) LogAssert.Expect(LogType.Error, EditModeDestroy);
        }

        private static readonly Regex EditModeDestroy = new("Destroy may not be called from edit mode");

        // --- What it refuses to be set up with ------------------------------------------------------

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
