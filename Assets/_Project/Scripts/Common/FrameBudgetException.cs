using System;

namespace Company.ChestGame.Common
{
    // A frame-budgeted loop was set up with something it cannot honestly run. Typed, because a test
    // asserting that a budget of zero is refused must not be satisfied by a NullReferenceException
    // from somewhere further in.
    //
    // Deliberately not under ChestGameException, for the reason PoolException is not either: a loop
    // handed no clock is a view that was never injected, not a delivery failure. It sits in Common
    // beside ChestGameException without being one - see the exception hierarchy in
    // docs/architecture.md.
    public class FrameBudgetException : InvalidOperationException
    {
        public FrameBudgetException(string message) : base(message) { }

        public static FrameBudgetException NoClock() =>
            new("A frame-budgeted loop advances frames through IGameClock, and was handed none");

        // Zero is not "no budget": with the budget checked after each unit it is one unit per frame,
        // the shape the class exists to avoid, while reading at the call site like switching the
        // budgeting off.
        public static FrameBudgetException BudgetNotPositive(double budgetMilliseconds) =>
            new($"A frame budget has to be more than zero milliseconds, got {budgetMilliseconds}");

        public static FrameBudgetException NoStep() =>
            new("A frame-budgeted loop needs a unit of work to run, and was handed none");

        public static FrameBudgetException NegativeCount(int count) =>
            new($"A frame-budgeted loop cannot run {count} units of work");
    }
}
