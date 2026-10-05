using System;

namespace Company.ChestGame.Pooling.Demo
{
    /// <summary>
    /// A race was set up, or asked to run something it cannot honestly do.
    /// </summary>
    /// <remarks>
    /// See docs/architecture.md, "Exception hierarchy".
    /// </remarks>
    public class PoolRaceException : InvalidOperationException
    {
        public PoolRaceException(string message) : base(message) { }

        public static PoolRaceException NoLanes() =>
            new("A race needs at least one lane to run, and was handed none");

        public static PoolRaceException DuplicateStrategy(PoolStrategy strategy) =>
            new($"A race cannot hold two lanes for the same strategy, got a second '{strategy}'");

        public static PoolRaceException NoClock() =>
            new("A race advances frames through IGameClock, and was handed none");

        public static PoolRaceException BudgetNotPositive(double budgetMilliseconds) =>
            new($"A race's frame budget has to be more than zero milliseconds, got {budgetMilliseconds}");

        public static PoolRaceException CountBelowOne(int count) =>
            new($"A race needs at least one item to place, got {count}");

        /// <remarks>
        /// See docs/pooling.md, "PoolRace's cancellation and identity traps".
        /// </remarks>
        public static PoolRaceException UnknownSoloStrategy(PoolStrategy strategy) =>
            new($"Solo mode asked for '{strategy}', which is not one of this race's lanes");

        public static PoolRaceException NoPool() =>
            new("A lane needs a pool to fill from, and was handed none");

        public static PoolRaceException NoFillParent() =>
            new("A lane needs a parent to fill placed instances into, and was handed none");

        /// <remarks>
        /// See docs/pooling.md, "PoolRaceException, and why authoring faults share one door".
        /// </remarks>
        public static PoolRaceException NoDocument() =>
            new("The pooling demo panel has no UIDocument assigned, so there is no chrome to bind to");

        public static PoolRaceException NoItemPrefab() =>
            new("The pooling demo panel has no item prefab assigned, so there is nothing to race");

        public static PoolRaceException LaneSlotCountMismatch(int expected, int actual) =>
            new($"The pooling demo panel needs one lane slot per strategy ({expected}), and has {actual}");

        public static PoolRaceException MissingElement(string type, string name) =>
            new($"PoolingDemo.uxml has no {type} named '{name}', so the panel cannot bind to it");

        public static PoolRaceException Disposed() =>
            new("This race has been disposed and cannot be started again");
    }
}
