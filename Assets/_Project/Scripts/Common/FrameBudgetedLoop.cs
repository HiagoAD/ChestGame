using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Company.ChestGame.Common
{
    /// <summary>
    /// Runs a fixed number of units of work across as many frames as they take, yielding whenever the
    /// work done since this frame started has passed a time budget.
    /// </summary>
    /// <remarks>
    /// See docs/architecture.md, "Engine seams: clock and random".
    /// </remarks>
    public class FrameBudgetedLoop
    {
        private readonly IGameClock _clock;
        private readonly double _budgetMilliseconds;

        public FrameBudgetedLoop(IGameClock clock, double budgetMilliseconds)
        {
            if (clock == null) throw FrameBudgetException.NoClock();
            if (budgetMilliseconds <= 0) throw FrameBudgetException.BudgetNotPositive(budgetMilliseconds);

            _clock = clock;
            _budgetMilliseconds = budgetMilliseconds;
        }

        /// <summary>
        /// Runs <paramref name="step"/> once for each index from 0 to <paramref name="count"/> - 1,
        /// yielding a frame whenever the elapsed time budget is exceeded.
        /// </summary>
        /// <exception cref="FrameBudgetException">
        /// <paramref name="step"/> is null, or <paramref name="count"/> is negative.
        /// </exception>
        /// <remarks>
        /// See docs/architecture.md, "Why RunAsync is split, and the ordering inside the loop".
        /// </remarks>
        public UniTask RunAsync(int count, Action<int> step, CancellationToken cancellationToken)
        {
            if (step == null) throw FrameBudgetException.NoStep();
            if (count < 0) throw FrameBudgetException.NegativeCount(count);

            return RunCoreAsync(count, step, cancellationToken);
        }

        /// <remarks>
        /// See docs/architecture.md, "Why RunAsync is split, and the ordering inside the loop".
        /// </remarks>
        private async UniTask RunCoreAsync(int count, Action<int> step, CancellationToken cancellationToken)
        {
            double frameStarted = _clock.ElapsedMilliseconds;

            for (int index = 0; index < count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                step(index);

                if (index + 1 == count) break;

                if (_clock.ElapsedMilliseconds - frameStarted < _budgetMilliseconds) continue;

                await _clock.NextFrame(cancellationToken);
                frameStarted = _clock.ElapsedMilliseconds;
            }
        }
    }
}
