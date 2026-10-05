using System;

namespace Company.ChestGame.Common
{
    /// <summary>
    /// A frame-budgeted loop was set up with something it cannot honestly run.
    /// </summary>
    /// <remarks>
    /// See docs/architecture.md, "Exception hierarchy".
    /// </remarks>
    public class FrameBudgetException : InvalidOperationException
    {
        public FrameBudgetException(string message) : base(message) { }

        public static FrameBudgetException NoClock() =>
            new("A frame-budgeted loop advances frames through IGameClock, and was handed none");

        /// <remarks>
        /// See docs/architecture.md, "Why RunAsync is split, and the ordering inside the loop".
        /// </remarks>
        public static FrameBudgetException BudgetNotPositive(double budgetMilliseconds) =>
            new($"A frame budget has to be more than zero milliseconds, got {budgetMilliseconds}");

        public static FrameBudgetException NoStep() =>
            new("A frame-budgeted loop needs a unit of work to run, and was handed none");

        public static FrameBudgetException NegativeCount(int count) =>
            new($"A frame-budgeted loop cannot run {count} units of work");
    }
}
