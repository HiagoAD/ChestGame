using System.Collections.Generic;

namespace Company.ChestGame.Pooling.Demo
{
    /// <summary>
    /// What one lane did during a race, snapshotted once its fill has settled.
    /// </summary>
    /// <remarks>
    /// See docs/design-decisions.md, "What the tests do and do not prove".
    /// See docs/pooling.md, "LaneMetrics, and why Instantiated/Destroyed are not what tests assert".
    /// </remarks>
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

    /// <summary>
    /// A finished race: one entry per lane that ran, in strategy order. Solo carries exactly one.
    /// </summary>
    /// <remarks>
    /// See docs/pooling.md, "RaceResult, and why Solo and FillMode travel with it".
    /// </remarks>
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
