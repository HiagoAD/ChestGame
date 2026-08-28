using System;

namespace Company.ChestGame.Pooling.Demo
{
    // A race was set up or asked to run something it cannot honestly do. Typed for the same reason
    // PoolException and FrameBudgetException are: a test asserting "this throws" must not be
    // satisfied by an unrelated NullReferenceException from somewhere inside the call.
    //
    // Deliberately not under ChestGameException, for the same reason those two are not. GameManager
    // catches exactly that base and turns whatever it caught into a content-download popup; a race
    // missing a clock, handed no lanes, or asked to solo a strategy it does not have is a demo wired
    // wrong, not a player-facing content failure.
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

        // Solo mode names a strategy rather than an index, so a caller can only ever ask for one of
        // the strategies this race actually has a lane for.
        public static PoolRaceException UnknownSoloStrategy(PoolStrategy strategy) =>
            new($"Solo mode asked for '{strategy}', which is not one of this race's lanes");

        public static PoolRaceException NoPool() =>
            new("A lane needs a pool to fill from, and was handed none");

        public static PoolRaceException NoFillParent() =>
            new("A lane needs a parent to fill placed instances into, and was handed none");

        public static PoolRaceException Disposed() =>
            new("This race has been disposed and cannot be started again");
    }
}
