using System;

namespace Company.ChestGame.Common
{
    // A frame-budgeted loop was set up with something it cannot honestly run. Typed, because a test
    // asserting that a budget of zero is refused must not be satisfied by a NullReferenceException
    // from somewhere further in.
    //
    // Deliberately not under ChestGameException, for the reason PoolException is not either. That
    // base is what GameManager catches to turn a failure into a content-unavailable popup before
    // treating it as handled, and a loop handed no clock is a view that was never injected. Reported
    // that way it would tell a player their download failed and swallow the bug that caused it, so it
    // sits in Common beside ChestGameException without being one, which is the whole point.
    public class FrameBudgetException : InvalidOperationException
    {
        public FrameBudgetException(string message) : base(message) { }

        public static FrameBudgetException NoClock() =>
            new("A frame-budgeted loop advances frames through IGameClock, and was handed none");

        // Zero is not "no budget". With the budget checked after each unit it is one unit per frame,
        // which is the shape the class exists to avoid, and it reads at the call site like a way to
        // switch the budgeting off.
        public static FrameBudgetException BudgetNotPositive(double budgetMilliseconds) =>
            new($"A frame budget has to be more than zero milliseconds, got {budgetMilliseconds}");

        public static FrameBudgetException NoStep() =>
            new("A frame-budgeted loop needs a unit of work to run, and was handed none");

        public static FrameBudgetException NegativeCount(int count) =>
            new($"A frame-budgeted loop cannot run {count} units of work");
    }
}
