using System.Collections.Generic;

namespace Company.ChestGame.Pooling.Demo
{
    // What one lane did during a race, snapshotted once its fill has settled. Instantiated and
    // Destroyed are display numbers - IPrefabPool's own running totals, read as a delta across the
    // timed fill - and are never what a test asserts against: a counter only proves a field moved.
    // The tests that matter count real Awake calls through a probe instead, the way
    // ChestBoardPoolingTests.SpawnProbe does.
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

    // A finished race: one entry per lane that ran, in strategy order. Solo carries exactly one;
    // running all four carries four. Solo travels on the result rather than only on the request that
    // started it, because the readout is built from the result alone and has to say which mode
    // produced its own figures without holding on to anything else.
    public readonly struct RaceResult
    {
        public IReadOnlyList<LaneMetrics> Lanes { get; }
        public bool Solo { get; }

        public RaceResult(IReadOnlyList<LaneMetrics> lanes, bool solo)
        {
            Lanes = lanes;
            Solo = solo;
        }
    }
}
