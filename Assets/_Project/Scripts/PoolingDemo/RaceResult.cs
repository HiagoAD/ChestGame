using System.Collections.Generic;

namespace Company.ChestGame.Pooling.Demo
{
    // What one lane did during a race, snapshotted once its fill has settled. Instantiated and
    // Destroyed are display numbers - IPrefabPool's running totals, read as a delta across the timed
    // fill - and are never what a test asserts against, because a counter only proves a field moved.
    // ChestBoardPoolingTests.SpawnProbe counts real Awake calls instead.
    public readonly struct LaneMetrics
    {
        public PoolStrategy Strategy { get; }
        public int RequestedCount { get; }
        public int PlacedCount { get; }
        public int Instantiated { get; }
        public int Destroyed { get; }
        public double ElapsedMilliseconds { get; }
        public int FramesUsed { get; }

        public LaneMetrics(PoolStrategy strategy, int requestedCount, int placedCount, int instantiated,
            int destroyed, double elapsedMilliseconds, int framesUsed)
        {
            Strategy = strategy;
            RequestedCount = requestedCount;
            PlacedCount = placedCount;
            Instantiated = instantiated;
            Destroyed = destroyed;
            ElapsedMilliseconds = elapsedMilliseconds;
            FramesUsed = framesUsed;
        }
    }

    // A finished race: one entry per lane that ran, in strategy order. Solo carries exactly one.
    // Solo and FillMode travel on the result rather than only on the request that started it, so the
    // readout labels a finished race with what it actually ran - a panel reading its own mutable
    // field would label it with whatever is selected when the result lands.
    public readonly struct RaceResult
    {
        public IReadOnlyList<LaneMetrics> Lanes { get; }
        public bool Solo { get; }
        public FillMode FillMode { get; }

        public RaceResult(IReadOnlyList<LaneMetrics> lanes, bool solo, FillMode fillMode)
        {
            Lanes = lanes;
            Solo = solo;
            FillMode = fillMode;
        }
    }
}
